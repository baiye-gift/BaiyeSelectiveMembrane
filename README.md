# 选择性分子筛 0.1.3 开发测试版

独立缺氧模组，包含1×1筛砖和1×2可预旋转筛门。完整边界由原生SimCellOccupier持续隔离；门动画不修改Sim格。已接入原创白银冰蓝复合膜、模块化承力框体、门夹具开闭和相邻接触节点。0.1.3本地安装状态见变更日志；源码公开与安装包、创意工坊发布是独立状态，尚未完成Unity/SimDLL验收。

## 项目与反馈

这是个人兴趣开发项目，当前为开发测试版本。上传源码用于跨设备开发与问题跟进，不承诺稳定性或修复时限。建议只在新测试存档中体验；使用已有存档前保留启用本模组之前的独立备份。禁用或退订不一定能恢复受影响的存档。

- 源码：[baiye-gift/BaiyeSelectiveMembrane](https://github.com/baiye-gift/BaiyeSelectiveMembrane)
- Bug 与建议：[GitHub Issues](https://github.com/baiye-gift/BaiyeSelectiveMembrane/issues)

反馈请附游戏/模组版本、DLC与其他模组、复现步骤、截图和Player.log；输运问题请补充两侧介质、输运方向与建筑配置。

本仓库包含源码、测试、原创素材和构建工具，不包含游戏DLL、已安装模组目录、存档或本机编译缓存。构建需要自行提供已安装游戏的Managed程序集。

## 使用

在“基地”菜单建造，研究归入高级过滤。默认白名单为空。

建造预览显示“左↔右”或“上↕下”及两侧端点轮廓，按游戏当前旋转键调整。顶边/底边墙砖需要上下输运，左右墙砖需要左右输运；角砖两轴都可能被相邻砖挡住。橙色端点表示实体或已排队墙砖阻挡，说明卡给出原因；青色只表示没有实体阻挡，不保证已经输运。单块砖选介质即可工作，不要求全网统一允许名单；没有四向自动扩散。

选中建筑，点击允许介质，选氧气后“确认并启用”即可保存，不再回主面板应用草稿。模式与主动方向直接切换；默认被动无需电力，暂停/恢复独立，选介质不自动解除用户暂停。未配置白名单仍隔离气液。主面板显示轴和等待原因，上限在“更多设置→上限”单独保存，其他操作不会提交未保存/非法上限。

主动模式需电线经过任一占用格；相邻筛砖/门组成供电网络，只选一个实际电路入口供电。部分付款按相同比例降低全网主动处理速度，不合并外部电路。

气体主动0.5 kg/s、120 W、0.5 kg缓存；液体主动2 kg/s、240 W、2 kg缓存。被动气体1/6 kg/s、液体2/3 kg/s，不免费向上输送液体。目标必须为同元素或真空，异种微量也会阻塞。

缓存锁定一个元素。氢气缓存未满而源侧变成水：停止取水，已有氢气等待兼容出口；两侧都是水时保持库存且不再索取工作电量。不同病菌和温度批次保持独立真实包，投递失败不清空库存。

模式/方向/删除白名单/降低上限可能进入等待排空状态。暂停筛分也暂停普通排空；拆除退料可零电力执行。侧栏有取消待配置、取消拆除入口。普通门权限保持原生面板；筛分配置复制不复制权限、门锁、暂停、入口优先级和库存。

## 构建与检查

需要.NET SDK 8和本机游戏Managed程序集，默认路径为D:/Steam/steamapps/common/OxygenNotIncluded/OxygenNotIncluded_Data/Managed。

克隆本仓库后，在仓库根目录执行。游戏安装路径不同可在构建时传入 `-p:GameManaged="<游戏Managed目录>"`；Native测试执行时将同一路径作为参数传入。素材检查需要安装Pillow的Python，只有重建动画时才另外需要kanimal-cli。

```powershell
dotnet run --project tests/Regression -c Release
dotnet build tests/Native -c Release -v:q -clp:ErrorsOnly
dotnet tests/Native/bin/Release/net8.0/MembraneNative.dll
powershell -ExecutionPolicy Bypass -File tools/build-package.ps1 -Python '<带Pillow的Python解释器路径>'
```

Native测试使用本机程序集；测试宿主单独引用SDK的System.Security.Permissions兼容库，并让游戏JSON库在.NET8测试中使用其原生晚绑定反射后端。生产模组不携带这些库，也不改变游戏JSON设置。测试不运行Unity或SimDLL。

运行素材已附在anim/assets。重建美术需Pillow和kanimal-cli：`python tools/build-anims.py --kanimal '<kanimal-cli.exe路径>'`，随后运行`python tools/verify-assets.py`。源图、导入坐标和可编辑SCML见[美术说明](art/README.md)。工具输出的PNG/GIF是图层合成预览，不是游戏截图。

## 当前验证边界

已实现质量事务、两建筑、实际付款差额、网络选主/备用、侧栏、保存前原生回调结清和世界级销毁恢复。最后两项以及投递的异种/实体格竞争、门锁中途退出、自动化接断线、旧存档、实际UI排布，必须按[实机清单](docs/playtest.md)验证；JSON往返和原生托管测试不能替代真实存档。

被动共享端点采用保守串行预约，降低争源过冲风险。备源回切采用独占付费试供，满供5秒后保留，失败退回并冷却。500气体单元满载应计入原版60 kW；尚未实测性能。

0.1.3 已实现常用设置直接提交、紧凑主面板、浅底深色说明、建造/选中输运轴及阻塞提示。57项逻辑回归与23项本机原生托管契约通过；原创素材不变，另有资源校验。两格门坐标、原生hover追加和蓝图占格路径已按游戏程序集检查，但实际箭头/框线、字体对比、UI缩放、拖建和真实氧气输运需按PLAYTEST进游戏验证。保存Schema和隔离事务没有变更；快速中断开闭的视觉跳变仍是既有实机调整项。本包用于开发测试，见[变更记录](CHANGELOG.md)。
