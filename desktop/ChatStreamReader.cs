using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PaperStudio
{
    // Chat Completions SSE is accepted only after the provider explicitly finishes.
    internal static class ChatStreamReader
    {
        const int MaxText = 2 * 1024 * 1024;
        const int MaxStreamChars = 50 * 1024 * 1024;

        public static async Task<string> Read(Stream stream, CancellationToken token, Action<int> progress = null)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            token.ThrowIfCancellationRequested();
            var cancelled = new TaskCompletionSource<bool>();
            using (token.Register(() => {
                cancelled.TrySetResult(true);
                try { stream.Dispose(); } catch { }
            }))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, true))
            {
                var state = new FrameState(progress);
                var line = new StringBuilder();
                var buffer = new char[4096];
                int total = 0;
                bool previousCr = false;
                try
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        var read = reader.ReadAsync(buffer, 0, buffer.Length);
                        if (await Task.WhenAny(read, cancelled.Task) != read)
                        {
                            // Some stream implementations ignore Dispose while a read is pending.
                            ObserveFault(read);
                            token.ThrowIfCancellationRequested();
                        }
                        int count = await read;
                        token.ThrowIfCancellationRequested();
                        if (count == 0)
                        {
                            if (line.Length > 0 && state.Line(line.ToString())) return state.Result();
                            if (state.Dispatch()) return state.Result();
                            throw new InvalidOperationException("文字接口流式连接提前结束，未收到完成标记；不完整方案未被采用，请重试。");
                        }
                        total += count;
                        if (total > MaxStreamChars) throw new InvalidOperationException("文字接口流式响应过大，已停止读取。");
                        for (int i = 0; i < count; i++)
                        {
                            char c = buffer[i];
                            if (c == '\n' && previousCr) { previousCr = false; continue; }
                            previousCr = c == '\r';
                            if (c == '\r' || c == '\n')
                            {
                                if (state.Line(line.ToString())) return state.Result();
                                line.Clear();
                            }
                            else
                            {
                                if (line.Length >= MaxText) throw new InvalidOperationException("文字接口流式事件过大，已停止读取。");
                                line.Append(c);
                            }
                        }
                    }
                }
                catch
                {
                    token.ThrowIfCancellationRequested();
                    throw;
                }
            }
        }

        static void ObserveFault(Task task)
        {
            task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        sealed class FrameState
        {
            readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaxText, RecursionLimit = 128 };
            readonly StringBuilder data = new StringBuilder();
            readonly StringBuilder content = new StringBuilder();
            readonly Action<int> progress;
            DateTime lastProgress = DateTime.MinValue;
            bool errorEvent;

            public FrameState(Action<int> progress) { this.progress = progress; }

            public bool Line(string line)
            {
                if (line.Length == 0) return Dispatch();
                if (line[0] == ':') return false;
                int colon = line.IndexOf(':');
                string field = colon < 0 ? line : line.Substring(0, colon);
                string value = colon < 0 ? "" : line.Substring(colon + 1);
                if (value.StartsWith(" ", StringComparison.Ordinal)) value = value.Substring(1);
                if (field == "event") errorEvent = value == "error";
                if (field == "data")
                {
                    if (data.Length + value.Length + 1 > MaxText) throw new InvalidOperationException("文字接口流式事件过大，已停止读取。");
                    if (data.Length > 0) data.Append('\n');
                    data.Append(value);
                }
                return false;
            }

            public bool Dispatch()
            {
                if (errorEvent) throw new InvalidOperationException("文字服务商在流式生成中返回错误；不完整方案未被采用，请检查服务商状态后重试。");
                errorEvent = false;
                if (data.Length == 0) return false;
                string payload = data.ToString().Trim();
                data.Clear();
                if (payload == "[DONE]") return true;
                Dictionary<string, object> root;
                try { root = json.DeserializeObject(payload) as Dictionary<string, object>; }
                catch (Exception ex)
                {
                    if (!(ex is ArgumentException) && !(ex is InvalidOperationException)) throw;
                    throw new InvalidOperationException("文字接口返回了无法解析的流式数据；不完整方案未被采用。");
                }
                if (root == null) throw new InvalidOperationException("文字接口返回了无效的流式数据。");
                object value;
                if (root.TryGetValue("error", out value) && value != null)
                    throw new InvalidOperationException("文字服务商在流式生成中返回错误；不完整方案未被采用，请检查服务商状态后重试。");
                if (!root.TryGetValue("choices", out value)) return false;
                var choices = value as object[];
                if (choices == null) throw new InvalidOperationException("文字接口返回了无效的流式方案格式。");
                foreach (object item in choices)
                {
                    var choice = item as Dictionary<string, object>;
                    if (choice == null) throw new InvalidOperationException("文字接口返回了无效的流式方案格式。");
                    object index;
                    if (choice.TryGetValue("index", out index) && !Object.Equals(index, 0)) continue;
                    if (choice.TryGetValue("delta", out value) && value != null)
                    {
                        var delta = value as Dictionary<string, object>;
                        if (delta == null) throw new InvalidOperationException("文字接口返回了无效的流式内容格式。");
                        object text;
                        if (delta.TryGetValue("content", out text) && text != null)
                        {
                            var part = text as string;
                            if (part == null) throw new InvalidOperationException("文字接口返回了不支持的流式内容格式。");
                            if (content.Length + part.Length > MaxText) throw new InvalidOperationException("文字接口生成内容过长，已停止读取。");
                            content.Append(part);
                            if (part.Length > 0 && progress != null && (DateTime.UtcNow - lastProgress).TotalMilliseconds >= 250)
                            {
                                lastProgress = DateTime.UtcNow;
                                progress(content.Length);
                            }
                        }
                    }
                    if (!choice.TryGetValue("finish_reason", out value) || value == null) continue;
                    if (Object.Equals(value, "stop")) return true;
                    if (Object.Equals(value, "length")) throw new InvalidOperationException("文字接口达到输出长度上限，方案尚未完整生成；不完整方案未被采用。");
                    if (Object.Equals(value, "content_filter")) throw new InvalidOperationException("文字服务商中止了内容生成；不完整方案未被采用。");
                    throw new InvalidOperationException("文字接口未正常完成方案生成；不完整方案未被采用。");
                }
                return false;
            }

            public string Result()
            {
                if (String.IsNullOrWhiteSpace(content.ToString())) throw new InvalidOperationException("文字接口已结束，但未返回方案内容，请重试。");
                if (progress != null) progress(content.Length);
                return json.Serialize(new { choices = new[] { new { message = new { content = content.ToString() } } } });
            }
        }
    }
}

