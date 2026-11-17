# Packaging Design Agent 客户端集成验收记录

本工程没有独立的 `packaging_agent` Python 服务。当前主 Agent 由 WPF 进程内的 `DesignWorkflow.cs` 和 `AgentOrchestration.cs` 编排，确定性几何另由随包 `python-tools/geometry.py` 复核。EXE 启动无需手动启动 Python 服务。

## 已验证

- “生成设计”进入 `DesignService.Generate(..., calculated: true)`，由主 Agent 检索、理解、计算、复核，再逐方案调用 `GenerateImage`。旧的非 Agent 草案入口未被客户端按钮调用。
- 中央七步状态来自 `AgentRun` 的实际工具开始、成功、失败、警告或取消事件。后台详情在 `%LOCALAPPDATA%/PaperStudio/agent-runs.jsonl`。
- 内置 `knowledge.json` 259 条、`web-knowledge.json` 17 条，合计 276 条；`knowledge-analysis.json` 也为 276 条。用户提到的 312 条与当前文件不一致，不能宣称已导入 312 条。
- 36 支、150 mm 长、15 mm 直径丙烯马克笔已执行本地真实知识库检索，得到材料、历史问题、合规与案例记录；3 组各 12 支单排的确定性装载尺寸也通过 Python 复核。这个检查不证明真实盒型的展开运动或打样可行。
- Windows QA 使用模拟 HTTP 走通三方案、图片显示、方案选择、修改生成新版本、草图、项目保存与导出；QA 图片为已有夹具，不是真实生成。
- 正式 EXE 已构建并启动；随包 Python 运行时和几何脚本存在，启动窗口可响应。

## 尚未通过真实验收

- 本机 `%LOCALAPPDATA%/PaperStudio/api-settings.json` 的文字及图片 API 基础地址为空，未检测到 `PAPER_TEXT_API_KEY` 和 `PAPER_IMAGE_API_KEY`，也没有可复用的已保存加密密钥。未调用真实文字或图片接口。
- 因此真实服务商生成三套方案、返回三张图片、客户端显示真实图片、第二轮真实生图，均未得到验证。不能用 QA 夹具结果代替。
- 图片模型对 36 支笔的实际数量、比例、闭合/展开一致性及草图一致性，仍需人工检查；程序只能验证结构化尺寸并要求视觉复核，不能把概念图认定为工程图或合规通过。

使用时在右上角设置实际 API Base URL、支持视觉与 JSON 的文字模型、支持 `images/generations` 和 `images/edits` 的图片模型及对应密钥。保存后密钥由 Windows 当前用户 DPAPI 加密；先做单图接入测试，再使用上述 36 支案例执行正式生成与修改验收。
