# 原生交互契约

| 能力 | 归属 | 状态与验证 |
|---|---|---|
| 配置宿主 | DetailsScreen.Config body + MembraneSideScreen | 独立场景实例；保留原条目；注册幂等原生托管检查 |
| 文本/按钮 | 原生 LocText / KButton 模板 | 沿用皮肤；主说明深色/弹层说明白色；常用操作直接生效，Unity布局待验 |
| 输入与焦点 | KInputTextField + KInputField 派生适配 | IME组合期不应用；不在状态刷新中重写字段；Unity焦点待验 |
| 上限草稿 | Core.EditSession，按实例持有 | 只草拟上限；原始非法文本保留，外部上限变化且有本地输入时标记冲突，常用设置变化合并；Regression |
| 常用设置 | Core.SettingsCommands.Quick | 从当前Applied复制，只改指定常用项，不提交上限草稿，不叠加Pending；Regression |
| 建造与选择提示 | MembraneFlowPreview + 原生BuildToolHoverTextCard | 轴/端点轮廓、实体与计划砖警告、重绑定旋转键，追加原生说明；静态适配和原生坐标测试，Unity待验 |
| 配置执行 | Core.BuildingState | 有库存时排空旧路径；可取消待配置；序列化Pending；Regression/JSON往返 |
| 元素选择 | 独立游戏overlay弹层 | 分类、发现过滤、搜索、当前列表批量动作；确认并启用直接提交允许名单；不连带上限；未知ID保留；代次/Revision保护 |
| 人物权限 | AccessControl + AccessControlSideScreen | 原生方向/个体权限，不跟随筛分复制；真实人物待验 |
| 网络批量 | MembraneSideScreen固定实例ID快照 | 新节点不加入，已删除/待配置/拆除节点跳过，逐类结果 |
| 拆除等待 | MembraneBuilding + 原生Deconstructable | 保存意图/优先级，侧栏明确取消，保留真实库存；任务实机待验 |

本契约不是Unity实测报告。数值0代表明确上限，null代表无限制；缺失元素记录不能被白名单操作静默删除。

0.1.3 常用设置取消两阶段草稿。选择介质仅作用当前建筑，暂停状态保持；确认时外部Revision变化则保留弹层并提示重新选择，不能用旧选择覆盖外部配置。上限保存先重新检查外部修订，克隆最新常用项，只更新两个上限；纯常用变化不清空上限输入，自身排空完成不误报冲突。更多设置关闭时不预留滚动高度。被动箭头代表输运轴，不承诺有实际流量；主动预览以已生效方向为准。
