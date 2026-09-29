# WarThunderChatTranslator

打雷的时候看不懂对面在讲什么飞机？这里可以实时翻译聊天对话！目前支持选择Yandex，微软或google翻译接口。

微软生草机belike：

![](doc/1.png)

![](doc/2.png)

![](doc/3.png)

## Todos

- [x] 重构至翻译界面WebUI
- [x] 支持目标语言切换
- [x] 提高代码复用性，减少重复
- [x] 实现网络代理设置
- [ ] 实现界面自定义
- [ ] 支持Bing Token
- [ ] 实现游戏内覆盖
- [ ] 实现GPT或DeepL等AI翻译（？）
- [ ] 实现自定义翻译接口（？）

---

## AI 版修改说明（Fork 说明）

> 本仓库是 [IShiraiKurokoI/WarThunderChatTranslator](https://github.com/IShiraiKurokoI/WarThunderChatTranslator)
> 的修改版（fork），基于上游 commit `5577362`，按原项目 GPL v3 许可证要求发布。
> 上游的 Todos 中「AI 翻译」「自定义翻译接口」「游戏内覆盖」几项在本分支中已实现。

**本分支的主要修改**（相对上游）：

1. **自定义 AI 翻译后端**：兼容任意 OpenAI 接口（LM Studio / Ollama / 在线 API 均可），
   与内置翻译（微软/Yandex/Google/Bing）并存可选；内置针对战争雷霆的默认提示词，
   自动处理社区黑话（BR=战评、BZ=战斗区域、spot=发现、cap=占点、overmatch=穿深溢出、
   蹲草/跳车/自爆 等），车名/地图名/玩家 ID 不翻译
2. **游戏内悬浮聊天窗**（对应上游 Todo「游戏内覆盖」）：
   独立 WPF 窗口，游戏前台时点击穿透不抢焦点；窗口内任意位置拖拽、12px 边缘缩放；
   背景不透明度 0-100%（文字恒不透明）、文字描边（开关/颜色/宽度）、
   可选显示英文原文、固定显示（常驻不淡出）；显示时长/淡出速度可调
3. **浮窗清空时机**：退出对局立即清空 / 下一局开始时清空（二选一），
   对局边界通过游戏本地端口 8111 的 `/map_info.json` 探测
4. **长按回车切换浮窗固定**：游戏在前台时按住回车 ≥0.5 秒松开即切换，
   「互动和操作」页可关闭
5. **翻译测试**：结果直接在设置页内显示（成功/失败/错误详情）；
   修复打包版 toast 通知 AUMID 错误导致通知被静默丢弃的问题
6. **打包**：自包含 MSIX（Windows App SDK 运行时内置），他人电脑双击即可安装
7. **文档**：新增 `CONTEXT.md`（术语表）与 `docs/adr/`（浮窗 WPF 方案决策记录）

**维护者**：[lyxx999](https://github.com/lyxx999)

*原项目作者为 IShiraiKurokoI，感谢其原始开发。*