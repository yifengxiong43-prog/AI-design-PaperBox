using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PaperStudio
{
    internal static class ChatStreamChecks
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static string Frame(string text, string finish = null, int index = 0)
        {
            return "data: " + new JavaScriptSerializer().Serialize(new { choices = new[] { new { index = index, delta = new { content = text }, finish_reason = finish } } }) + "\n\n";
        }
        static async Task Reject(string input, string expected)
        {
            bool rejected = false;
            try { await ChatStreamReader.Read(new ChunkStream(input, 3, false), CancellationToken.None); }
            catch (InvalidOperationException ex) { rejected = ex.Message.Contains(expected) && !ex.Message.Contains("SECRET"); }
            Check(rejected, "SSE must reject safely: " + expected);
        }
        public static async Task Run()
        {
            string stream = ": heartbeat\r\n\r\ndata: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\"}}]}\n\n" +
                Frame("WRONG", null, 1) + Frame("纸盒") + ": keepalive\n\n" + Frame(" packaging") + Frame(null, "stop");
            int last = 0;
            string output = await ChatStreamReader.Read(new ChunkStream(stream, 1, true), CancellationToken.None, n => { Check(n >= last, "Progress must be monotonic"); last = n; });
            dynamic decoded = new JavaScriptSerializer().DeserializeObject(output);
            Check((string)decoded["choices"][0]["message"]["content"] == "纸盒 packaging" && last == 12, "UTF-8 split, role-only, other choices, heartbeat and early stop");
            string done = await ChatStreamReader.Read(new ChunkStream(Frame("ok") + "data: [DONE]\n\n", 2, true), CancellationToken.None);
            Check(done.Contains("ok"), "DONE completion");
            string eof = await ChatStreamReader.Read(new ChunkStream(Frame("ok") + "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}", 17, false), CancellationToken.None);
            Check(eof.Contains("ok"), "Final event without newline");
            string multiline = "data: {\"choices\":\rdata: [{\"delta\":{\"content\":\"yes\"},\"finish_reason\":\"stop\"}]}\r\r";
            Check((await ChatStreamReader.Read(new ChunkStream(multiline, 1, false), CancellationToken.None)).Contains("yes"), "Multiline SSE and CR separators");
            await Reject(Frame("partial"), "提前结束");
            await Reject(Frame("partial") + Frame(null, "length"), "长度上限");
            await Reject(Frame("partial") + Frame(null, "content_filter"), "中止");
            await Reject(Frame("partial") + Frame(null, "tool_calls"), "未正常完成");
            await Reject("data: [DONE]\n\n", "未返回");
            await Reject("data: {\"error\":{\"message\":\"SECRET\"}}\n\n", "返回错误");
            await Reject("event: error\ndata: SECRET\n\n", "返回错误");
            await Reject("data: {SECRET}\n\n", "无法解析");
            await Reject("data: " + new string('x', 2 * 1024 * 1024 + 1), "过大");
            await Reject(Frame(new string('x', 1100000)) + Frame(new string('x', 1100000)), "内容过长");
            using (var source = new CancellationTokenSource())
            {
                var blocked = new ChunkStream(Frame("partial"), 4096, true);
                var pending = ChatStreamReader.Read(blocked, source.Token);
                source.Cancel();
                bool cancelled = false;
                try { await pending; }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && blocked.Disposed, "Cancellation must interrupt blocked read even if stream ignores disposal");
            }
            Console.WriteLine("Chat stream parser checks passed.");
        }

        sealed class ChunkStream : Stream
        {
            readonly byte[] data;
            readonly int chunk;
            readonly bool blockAtEnd;
            int position;
            public bool Disposed;
            public ChunkStream(string data, int chunk, bool blockAtEnd) { this.data = Encoding.UTF8.GetBytes(data); this.chunk = chunk; this.blockAtEnd = blockAtEnd; }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            {
                if (position == data.Length && blockAtEnd) return new TaskCompletionSource<int>().Task;
                return Task.FromResult(Read(buffer, offset, count));
            }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int take = Math.Min(Math.Min(count, chunk), data.Length - position);
                Array.Copy(data, position, buffer, offset, take); position += take; return take;
            }
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return data.Length; } }
            public override long Position { get { return position; } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}
