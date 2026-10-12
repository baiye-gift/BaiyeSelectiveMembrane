# 开发变更

## 2026-10-13 — 独立 GitHub 源码仓库发布

用户确认新建公开 `baiye-gift/BaiyeSelectiveMembrane` 并回复“上传”。本次在独立导出副本操作，原工作区和已安装mod只读取，不改变原仓库Git状态或运行资源。导出87个源码/测试/原创素材/工具/文档文件，逐文件SHA-256与原文件一致；排除bin、obj、dist、本机审计报告，无游戏/依赖DLL。

实际修改导出副本的README.md（个人开发测试说明、仓库/Issues地址、独立构建入口）、.gitignore（补充本机缓存/密钥/存档排除）、本日志；新增简短AGENTS.md。src、tests、anim、art及构建工具原样保留，版本仍为0.1.3，未改变玩法、存档ID或库存。

上传前检查：`dotnet build SelectiveMembrane.csproj -c Release`退出0，0错误、2个既有MSB3277程序集冲突警告；`dotnet run --project tests/Regression -c Release`的57项逻辑回归通过；`dotnet build tests/Native -c Release`及执行MembraneNative.dll退出0，23项本机原生托管契约通过（不运行Unity/SimDLL）；带Pillow的Python执行`tools/verify-assets.py`通过2549项素材断言。git diff --cached --check通过，88暂存文件无bin/obj/dist、DLL/PDB/存档/密钥或本机审计报告；暂存文本凭据/私钥特征扫描无匹配，仅Git行尾转换提示。

已创建公开仓库 https://github.com/baiye-gift/BaiyeSelectiveMembrane ，main默认分支，Issues已启用；反馈入口 https://github.com/baiye-gift/BaiyeSelectiveMembrane/issues 。首个源码提交`9396879d88d7671f9b7c1ae1a4fb46082e5bf952`成功推送，GitHub API核对远端SHA与本地一致，88个远端文件路径及Git blob SHA全部一致，Issues端点可访问。完成后补齐本日志并推送独立文档提交，src/tests/anim/art/tools不变。

原分子筛工作区87份导出源文件哈希与操作前一致，未修改原项目、父仓库Git状态或已安装mod；独立导出副本保留便于后续检视。未创建GitHub Release，未安装或启动游戏；真实输运、旧存档、UI及Unity/SimDLL验收仍待PLAYTEST。

## 2026-10-06 — 0.1.3 本地安装

用户“安装”明确授权本次替换。检查游戏已退出、原安装为0.1.2，原0.1.3包SHA-256与配套文件及开发记录一致。备份原安装完整12文件到 `dist/install-backups/BaiyeSelectiveMembrane-0.1.2-20261006-161515`（游戏扫描目录之外），备份逐文件校验通过；按12文件白名单安装到 `D:/Documents/Klei/OxygenNotIncluded/mods/local/BaiyeSelectiveMembrane`，安装内容与ZIP逐文件SHA-256一致，版本0.1.3，mods.json哈希保持不变。

源记录更新 `README.md`、本CHANGELOG及根 `docs/ai-change-log.md`；同步分发目录/ZIP/.sha256和安装目录中的README/CHANGELOG，消除“尚未安装”旧状态，DLL与美术资源保持开发包内容。安装与元数据核对记录保存在 `dist/install-staging/0.1.3-20261006-161515/verification.json`，最终包SHA-256见根日志。

本次安装未启动游戏、未修改存档/其他模组/启用设置。既有57项逻辑回归与23项托管检查不代表实机验收；实际侧栏、施工轴线和氧气输运仍需PLAYTEST。

## 0.1.3 — 2026-10-05 — 常用设置直接生效与施工输运轴提示

用户指出0.1.2主面板过长且难懂，并确认仅给默认左右输运的顶边砖选氧气；左右相邻实体阻塞，原界面未说明。用户进一步要求减少实际步骤且建造前看清方向，确认修订方案后实施。范围为侧栏交互、建造/选中显示、等待诊断及相关检查，不修改美术素材、存档Schema或四向输运规则；开发当日未安装，10月6日安装见上方记录。

- 修改 `src/UI/MembraneSideScreen.cs`：介质弹层一次“确认并启用”直接提交完整允许名单，模式/主动方向直接切换；常用设置无草稿应用行。主面板状态/轴/介质/模式暂停/更多，主动方向与门锁条件显示；更多默认关闭不占高度，展开四个短标签共用单一滚动正文。说明字体15且浅底深色，弹层白字，修复原按钮白字继承造成不可见。上限单独保存/撤销，有修改/非法/冲突才显示动作。提交前观察最新修订；介质弹层Revision、实例与代次保护，外部复制后拒绝旧集合；原隐藏根对象、输入/IME与关闭生命周期修复保留。
- 新增 `src/Core/FlowGuidance.cs`：共用端点轴/边界计算、等待原因及安全常用配置命令（克隆Applied，仅变指定项）。修改 `src/Core/MembraneSettings.cs`：上限会话观察与常用项合并分开，保持非法/未保存上限和独立优先级文本，自身排空完成不误报冲突；外部上限冲突显式重载。修改 `src/Game/MembraneBuilding.cs`：输运几何共用，等待原因具体化，门任一单元有工作不误报全门阻塞。
- 新增 `src/Game/MembraneFlowPreview.cs`，修改 `src/Game/MembraneMod.cs`：砖/门的原生建造预览、施工蓝图和完成态附诊断显示，只在预览或选中时画两侧框与轴线，跨世界/边界保护；被动双向，已完成主动按真实Applied单向。实体及计划墙砖计入阻塞；施工模板独立，计划建筑通过Building.Def.BuildingComplete查Sim占格能力。原生BuildToolHoverTextCard追加轴/阻塞/重绑定旋转键，不重置绘制池。隐藏/销毁释放线对象和独立材质，图形不改Sim和朝向。
- 修改 `tests/Regression/Program.cs`：7项新增回归覆盖轴/行边界、截图顶砖单独选氧气、非法上限隔离、旧库存排空与Pending拒绝、外部变更冲突/合并、干净上限同步/独立优先级保留、异种/上限/重力/平衡原因。修改 `tests/Native/Program.cs`：3项新增托管检查覆盖原生旋转门两格坐标、三类Prefab挂接/选择/旋转/原生卡追加、确认直接提交且不调用上限捕获。
- 更新 `mod.yaml`、`mod_info.yaml`（0.1.3）、`README.md`、`DESIGN.md`、`UX-CONTRACT.md`、`docs/playtest.md`、本CHANGELOG、根 `docs/选择性分子筛系统设计规格.md` 第16节/相关验收文字和 `docs/ai-change-log.md`。原桌面规格未改。

检查：57项逻辑回归、23项本机U59-744825托管契约通过；Release/Native构建0错误，2个既有游戏程序集警告。独立只读审核发现初稿BeginDrawing重置原生建造卡，已改用既有drawer追加并纳入契约检查；确认原生Overlay摄像机、施工模板和根格旋转路径，未发现另一项证据充分Important/Critical。frontend-design-premium strict静态审计0 findings；该审计针对网页模式，不能证明C#游戏UI。素材2549条资源断言通过，12文件包白名单/逐文件SHA-256核对通过，只有自有DLL，六个原创素材与0.1.2一致；最终包校验值见根开发日志。

未验证：未运行Unity UI/SimDLL，实际文字对比/高度、箭头Shader与遮挡、短屏和缩放、拖建计划砖、点击焦点/IME、外部配置竞争、真实氧气输运和旧存档需按PLAYTEST复测。逻辑用例证明预期规则，不证明用户存档中原生回调已发生。本版仍为开发测试版。

## 0.1.2 — 2026-10-05 — 配置侧栏空引用与生命周期修复

用户提供选中门的NullReferenceException后确认“修复，并且再检查是否还有其他的明显bug”。本次修复UI初始化、隐藏恢复、弹层/输入生命周期及配置草稿显示；保持既有膜美术、气液事务、供电预算、权限和保存Schema不变。安装前备份原0.1.1，实际安装结果写入根开发记录。

- 根因证据：本机Player.log记录 `MembraneSideScreen.RefreshView [0x0011e]`；安装DLL与原构建DLL SHA-256一致。反编译定位为 `GetComponentInParent<KButton>()` 后取gameObject，原生DetailsScreen在SetTarget时侧栏尚未激活，默认父级查找跳过inactive对象，因此返回null。另两处取消按钮有同类问题。
- 修改 `src/UI/MembraneSideScreen.cs`：保存完整按钮克隆根对象，三处显隐直接使用缓存，弹层/过滤行释放时同步清理根对象与文字绑定；所有控件建完才置built，构建期回调与未就绪目标不刷新；销毁目标后关弹层，停用行的旧回调不再生效。
- 独立复核发现原生DetailsScreen/页签通过SetActive隐藏，未必调用OnShow；增加OnCmpDisable结束输入并关闭独立overlay弹层，保留实例草稿，OnCleanUp才清理会话与缓存。输入桥接在对象激活前设置唯一新owner的activateOnSpawn，沿用原生屏幕输入栈，避免游戏快捷键穿透；关闭弹层不向隐藏按钮恢复焦点。
- 修改 `src/Core/MembraneSettings.cs`：重载显示Pending或Applied的真实目标配置；成功创建Pending也推进Revision，拒绝请求不推进；Observe比较Pending优先的目标值，相同则认领版本而不重写原始文本，不同才置过期。自身排空完成不误报外部修改；外部待配置明确提示草稿过期，允许只读期间显式重载Pending，不自动覆盖编辑文本。
- 修改 `tests/Regression/Program.cs`：新增首次Pending显示、自身排空完成、外部Pending对已编辑/未编辑草稿的过期与显式重载3项用例。修改 `tests/Native/Program.cs`：新增隐藏控件根引用、构建完成时机、直接停用关弹层/绑定清理、输入owner激活4项静态适配契约。旧实现先出现隐藏查找失败、Pending显示失败；追加外部Pending测试在首轮修复下仍失败，配套修复后通过。
- 修改 `mod.yaml`、`mod_info.yaml`、`README.md`、`docs/playtest.md`、本CHANGELOG；版本0.1.2，增加首次选中/重选、主动被动切换、隐藏取消按钮恢复、切目标/页签/销毁关弹层、输入快捷键/IME、Pending显示与冲突的实机复测场景。根规格和开发日志同步实际状态。

已检查：50项逻辑回归和20项本机U59-744825原生托管契约通过，Native/Release构建0错误、2个既有游戏程序集警告。独立只读审核确认修复方向与原生生命周期；未发现另一个证据充分的Game/Core崩溃。素材不变，打包时继续执行2549条资源断言和12文件白名单/逐项校验。

未验证：未运行Unity UI或SimDLL；实际首次选中、隐藏恢复、弹层、快捷键/中文输入仍需用户实机复测。静态适配契约不能代替点击复现；本版仍为开发测试版。

## 2026-10-05 — 0.1.1 安装前描述修订

用户“安装”授权本地安装。检查时游戏已退出，mods/local中尚无本mod。修正 `mod.yaml` 中遗留的“0.1.0、原版占位外观”简介，使其与实际0.1.1原创素材一致；仅更新描述，不修改代码/动画/保存格式。随后重新运行 `tools/build-package.ps1` 的素材校验、Release构建、12文件白名单与逐项SHA-256核对，再进行本地安装。最终包校验值、安装文件逐项检查和实际安装结果追加到根 `docs/ai-change-log.md`；不启动游戏，不将安装视为实机验收。

## 0.1.1 — 2026-10-05 — 白银冰蓝原创膜组件接入

用户确认“可以的，开始开发”，范围为已确认外观的生产素材、砖/门动画、拼接显示、状态及独立测试包。未安装、提交、推送或发布；既有气液事务、供电计算、人物权限和Sim隔离保持原实现。

- 新增 `art/reference-approved.png`、`art/source/{hardware-sheet.png,film-sheet.png,prompts.md}`、`art/README.md`、`art/import-manifest.json`，以及两个组件目录中的PNG/SCML。内置image_gen制作原始透明图，项目保留原图；硬件有分段白银承力块、角部张紧座、细压紧臂、铜橙母线节点和多灯冰蓝模块，膜有低对比细孔/层压纹及规则纵向通行带。
- 新增 `tools/{build-anims.py,verify-assets.py}`、`anim/assets/baiye_membrane_tile/` 和 `baiye_membrane_door/` 下共6个运行文件。机械导入使用透明裁切、缩放和九宫格布局，膜alpha乘0.68并烘焙过渡强度；砖16种邻接，门6帧开/关各0.3秒，所有帧膜持续存在。输出 `dist/verification/{art-preview.png,door-transition.gif}`，为源图层合成预览。
- 新增 `src/Core/VisualPolicy.cs`、`src/Game/MembraneVisual.cs`；修改 `src/Game/{MembraneMod.cs,MembraneDoor.cs,MembranePower.cs}`。自有KAnim替换原版占位图集；仅已完成膜建筑加入显示登记表，出生/销毁局部更新，普通砖/预览/跨世界不连接。旋转映射沿用原生R90顺时针；接点各持有格内半组件，砖与门相邻形成完整节点；门自身两格之间无外部接点。状态依据实际付款/输运/暂停/排空显示，主动输运才出现小方向标，世界退出清理登记表。
- 门开闭委托显示层播放并排队终态，不调用Sim修改接口。独立审核发现旅客被死亡/消失/读档校正移除后显示可能永久打开，已改为每200ms按 `Travelers != 0` 校正两个结果。快速反向目前从过渡端点重播，会有短暂跳变；终态正确，平滑衔接列入实机调整项。
- 修改 `tests/{Regression,Native}/Program.cs`：新增5项四向/旋转/显示规则回归和4项自有动画/原生接口契约，后者补查零旅客时关闭显示的适配调用。更新 `tools/build-package.ps1`、`mod_info.yaml`、`README.md`、`DESIGN.md`、`docs/playtest.md`；打包先验证素材，只加入6个自有动画资源及原有6个文件，逐文件白名单/重复检测与SHA-256核对。
- 项目外记录：新增 `docs/superpowers/plans/2026-10-05-selective-membrane-art.md`；更新根 `docs/ai-change-log.md`。未修改原桌面设计文档或其他mod。

已执行：47项回归、16项本机U59-744825原生托管契约通过；素材2549条断言通过，包含真实ANIM/BILD符号帧引用、原生矩阵变换占格、半连接件接缝及膜图集alpha。新增规则起初因缺少VisualPolicy无法编译，实现后通过。导出器的旧ANIM/BILD总数头字段为0；查原生解析器确认忽略这些字段后，检查改为实际帧/符号和有效可见元素数，没有伪改导出数据。Release构建0错误、2个既有游戏程序集版本警告。独立只读审核无Critical项，重要显示问题已修。

包最终逐项核验和SHA-256见同目录dist输出及根开发记录。未执行Unity/SimDLL；半透明Shader、人物遮挡、选中高亮、实际施工/替换、门中断/死亡/读档、旧存档和UI实机布局仍需执行PLAYTEST。本版是开发测试包，不能视为正式V1验收。

## 0.1.0 — 2026-10-05

- 新建独立模组、两建筑、气液单元素事务、真实库存/尾量/温度/多病菌包、投递确认和失败重试。
- 加入被动重力/差值规则、共享端点事务预约；主动锁定缓存、按实际付款比例工作、相邻拓扑与主备入口。
- 接入原生INavDoor/AccessControl、自动化与手动锁、旧旅客退出/拒绝新开始；普通拆除等待退料、保存优先级、取消入口和最终保护。
- 配置应用与排空、实例草稿、原始非法数值、分组侧栏、发现元素搜索弹层、配置复制保护和固定快照网络批量操作。
- 保存前结清原生回调；真实库存用原生保存字段持有JSON快照，销毁未交付库存由世界恢复组件接管。该路径仍需真实存档与Sim验证。
- 开发占位使用原版素材，不含原创美术。测试及实机限制详见README和PLAYTEST。

验证：42项守恒/行为回归、12项本机原生托管契约通过；Release构建0错误、2个既有游戏程序集版本警告。独立审核发现的争源、未知元素事务、门锁/拆除、非法草稿和UI回调问题已修；追加读档占格者退出资格重建。包检查为6项、仅自有DLL、逐项SHA-256一致。不将本版标为正式V1验收通过。
