# Packaging Design Agent 工作流

当前客户端采用一个主 Agent 编排流程，不拆分成多个互相传话的模型。主 Agent 按任务需要使用以下程序工具：

| 工具 | 作用 | 执行者 |
|---|---|---|
| `retrieve_references` | 从内置原始资料和缓存分析中检索案例、材料、历史问题、合规记录，并建立本轮短引用目录 | 程序 |
| `understand_request` | 结合自然语言和 reference 图片提取硬要求、禁止项、盒型/开合/布局/风格约束 | 文字 API + 程序校验 |
| `calculate_geometry` | 根据支数、笔长、笔径、间隙、壁厚和运输上限计算单元及外壳尺寸，再由随包 Python 工具独立核对单排尺寸、完整笔长和总支数 | `AssemblyGeometry` + `python-tools/geometry.py` |
| `validate_manufacturing` | 校验材料、工艺、风险和测试是否来自本轮真实资料 | `CitationCatalog` + `CitationGuard` |
| `review_design` | 独立检查需求落实、结构可行性和不同方向 | 文字 API + 程序校验 |
| `generate_image` | 按已通过的结构账本调用图片 API | 图片 API |
| `inspect_image` | 检查笔数、完整笔身比例、结构一致性、草图尺寸和 reference 偏离 | 图片 API + `ImageInspection` |
| `revise_design` | 使用已通过版本和具体失败原因，只修改指定部分 | 主 Agent 的有界回修 |

每次调用都写入 Agent 运行日志，包含工具名、阶段、输入摘要、结果状态、重试次数和错误原因；API 密钥、图片像素和完整原文不写入日志。检查点只保存已绑定的稳定资料 ID，不保存本轮短引用编号。

模型不拥有本地文件访问权，也不直接执行工具。工具的输入在 API 边界解析后由程序计算和校验；模型返回的尺寸、来源 UUID、原文摘录和图片质量判断均属于不可信结果。关键引用或结构校验失败时保留完整错误信息并在有限重试后停止，非关键案例可在关键资料仍完整时跳过。
