# 白银冰蓝复合膜素材

本次使用内置 image_gen，以用户确认的 `reference-approved.png` 为结构、比例、配色和机械语言参考，制作独立硬件及膜材质透明源图。未使用原版游戏动画、图集或第三方角色素材。以下生成文件已复制到本项目，运行时不依赖 Codex 缓存路径。

| 文件 | 来源 | 用途 |
| --- | --- | --- |
| reference-approved.png | 本会话已确认概念图，exec-9f92ac6b-a847-4800-82cb-c5a36aa0aa6f.png | 外观参考 |
| source/hardware-sheet.png | 内置生成，exec-51fa6272-9913-405e-a467-4538d2cc66fd.png | 空心承力框、压紧臂、母线节点、状态模块 |
| source/film-sheet.png | 内置生成，exec-5404449f-df8c-4a4f-b15e-ce28c83549ff.png | 安静膜和规则纵向通行带 |
| source/baiye_membrane_tile/ | build-anims.py机械导入输出 | 组件PNG和可编辑SCML |
| source/baiye_membrane_door/ | build-anims.py机械导入输出 | 组件PNG和可编辑SCML |

制作提示词整理见 [source/prompts.md](source/prompts.md)。原始源图保持不变。导入脚本记录裁切坐标，执行透明边界裁切、缩放、九宫格框体布局及alpha烘焙；极小方向三角是程序生成的功能图标。膜alpha乘数0.68，各过渡帧以源膜强度分档烘焙，避免依赖导出器未保留的SCML对象alpha。四边接点拆成格内半节点，两建筑相接形成完整插接件，没有越格覆盖。

`anim/assets` 下两套各三个文件是运行资源，测试包仅携带这些编译资源，不携带源图/SCML、参考图或工具。砖图集512×512、门图集2048×1024；砖100×100、门100×200逻辑单位，导出器坐标倍率2与游戏默认0.005缩放相配，最终占格1×1和1×2。

重建：带Pillow的Python执行 `tools/build-anims.py --kanimal <kanimal-cli.exe>`，随后运行 `tools/verify-assets.py`。检查会读取真实ANIM/BILD，按原生解析规则验证符号帧引用、矩阵变换后的占格、接点边界及膜图集alpha。工具输出 `dist/verification/art-preview.png` 和 `door-transition.gif` 仅是同一源布局的图层合成，不能证明Unity渲染、人物遮挡或实机预览正确。
