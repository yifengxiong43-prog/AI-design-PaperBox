# 资料身份与版本

每条知识记录保存独立 UUID `StableId`。`Id` 继续作为旧版来源别名（文件名/工作表/行），用于旧项目兼容和定位；它不是跨版本永久身份。`SourceContentHash` 校验精确的 Group 和 Text，分析库仍使用旧 Id，CorpusHash 随资源文件重建。

重导时，唯一且完全相同的 Group + Text 沿用旧 UUID，因此文件改名或行号移动不改变身份。内容或类别改变时分配新 UUID，不能把旧身份绑定到新文本。相同内容重复出现时，各记录始终使用不同 UUID；仅原 Id 也唯一相同的重复记录可沿用，无法分辨的移动记录分配新 UUID，不按顺序猜测。旧别名随来源版本解释，不应跨版本自动指向另一行内容。

`import_references.py` 从覆盖前的 knowledge.json 继承身份；`build_knowledge_analysis.py` 为新记录补身份并刷新分析指纹，`--check` 只读校验、不分配 UUID。不得删除旧知识 JSON 再导入，否则无法恢复既有 UUID。直接修改 web-knowledge.json 的正文后重建时，旧内容指纹失配会使该条获得新身份。

验证：运行 `python desktop/check_stable_source_ids.py`，再运行 `python desktop/build_knowledge_analysis.py --check`。
