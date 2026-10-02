# 战争雷霆聊天翻译器（AI 版）

> 本仓库是 [IShiraiKurokoI/WarThunderChatTranslator](https://github.com/IShiraiKurokoI/WarThunderChatTranslator)
> 的修改版（fork），基于上游 commit `5577362`，按原项目 GPL v3 许可证发布。维护者：[lyxx999](https://github.com/lyxx999)。
> 上游 Todos 里的「实现游戏内覆盖」「实现 GPT 或 DeepL 等 AI 翻译」「实现自定义翻译接口」在本分支已全部完成。

打雷的时候看不懂对面在讲什么飞机？这里可以实时翻译聊天对话。
内置 Microsoft Azure / Yandex / Google，也可以接任意 OpenAI 兼容接口（LM Studio、Ollama、在线 API 都行），
并且带一个**游戏内悬浮聊天窗**：翻译结果直接飘在游戏画面上，不用切窗口。

微软生草机belike：

![](doc/1.png)

![](doc/2.png)

![](doc/3.png)

## 安装 / 升级

1. 到本仓库 [Releases](https://github.com/lyxx999/WarThunderChatTranslator/releases) 下载 `WarThunderChatTranslator-AI-<版本号>.zip`
2. 解压后双击「安装.bat」→ 确认 UAC → 等待"安装成功"
   （脚本会把包内 `wct-sign.cer` 导入本机「受信任的人」证书存储，这是 Windows 安装非商店签名包的正常步骤，只影响本机对这个包的信任）
3. 开始菜单搜索「战争雷霆聊天翻译器」启动，然后进游戏

- **系统要求**：Windows 10 1903+ / Windows 11，64 位
- 自包含 MSIX，Windows App SDK 运行时已打进包里，不需要额外装运行库
- 升级直接重装新版即可，**设置全部保留**
- 聊天数据来自游戏自己的本地接口（`127.0.0.1:8111`），所以必须游戏在跑
- 程序内「翻译器更新」检查的就是本仓库 Releases；包内「说明.txt」是完整使用手册

## 功能

### 翻译

- **后端可选**：Microsoft Azure（默认）/ Yandex / Google / 自定义 AI（OpenAI 兼容接口）
  - LM Studio：`http://localhost:16324/v1` + 你加载的模型名
  - Ollama：`http://localhost:11434/v1`
  - 在线 API：BaseUrl + API Key + 模型名；接口路径默认 `/chat/completions`
- **战争雷霆专用默认提示词**：自动处理社区黑话（BR=战评、BZ=战斗区域、spot=发现、cap=占点、
  overmatch=穿深溢出、蹲草/跳车/自爆 等），车名/地图名/玩家 ID 不翻译；
  「自定义 AI · 系统提示词」留空就用它，也可以自己写
- **目标语言可选**（默认简体中文），并有语种兜底：译文里不含目标语言的字符时直接显示原文，
  避免"中文被翻成英文"
- **测试翻译**：API 页底部按钮，结果就地显示（成功/失败/错误详情），不用回去翻日志

### 游戏内悬浮窗

- 独立窗口，游戏在前台时**点击穿透、不抢焦点**；也可以设置成"游戏不在前台时隐藏"，切回游戏自动恢复
- **分段配色对齐游戏内聊天**：频道标识与昵称用发送方阵营色；无线电快捷指令的正文也用阵营色，
  玩家手打的消息正文与时间戳用中性色；正文里的网格坐标（`[b2]`、`[ka1, 高度 600 米]`）单独一色。
  友军/敌军/系统/中性/坐标**五种颜色都可自定义**，每项都带「恢复默认」
- **无线电快捷指令识别**：主判据是游戏原文里的字距 `\t`（游戏给中文客户端的本地化文案逐字符插字距，
  手打文字没有），其余客户端按游戏 `lang/ui.csv` 的 `voice_message_*` 文案表兜底
- 可调项：显示时长、淡出速度、背景不透明度（0-100%，文字恒不透明）、行间距
  （0 = 上下两排字刚好贴在一起）、字体 / 字号 / 文字样式（正常～加粗）、文字描边（开关/颜色/宽度）
- 显示原文（译文下方小字附原句）、显示阵营标识（`[友军]`/`[所有人]`）、固定显示（常驻不淡出）
- 清空时机二选一：退出对局立即清空 / 下一局开始时清空（对局边界由游戏本地端口探测）
- 窗口内任意位置拖拽移动、边缘或角落缩放、滚轮翻看旧消息
- **长按回车切换固定显示**：游戏在前台时把回车按住到设定时长（默认 0.5 秒，可调 0.1-5 秒）再松开，
  不需要打开设置窗口
- 所有滑块旁边都能直接键入精确数值

### 其他

- 网络代理设置（地址/账号/密码）
- 主题与背景自定义
- 浏览器面板（上游的 WebUI）保留，与浮窗相互独立、可同时使用
- 托盘图标常驻

## Todos

- [x] 重构至翻译界面 WebUI
- [x] 支持目标语言切换
- [x] 提高代码复用性，减少重复
- [x] 实现网络代理设置
- [ ] 实现界面自定义
- [ ] 支持 Bing Token
- [x] 实现游戏内覆盖（本分支：悬浮聊天窗）
- [x] 实现 GPT 或 DeepL 等 AI 翻译（本分支：任意 OpenAI 兼容接口 + 战雷专用提示词）
- [x] 实现自定义翻译接口（本分支：同上，并保留内置翻译可选）

## 给开发者

- 解决方案 `WarThunderChatTranslator.sln`，VS 2022 / x64 / Release
  - `WarThunderChatTranslator`：主程序，WinUI 3 + Windows App SDK 1.5（设置页、托盘、本地 HTTP 服务与浏览器面板）
  - `WarThunderChatTranslator.Float`：浮窗，WPF 类库，被主程序 `ProjectReference` 引用
    （为什么浮窗不用 WinUI 实现，见 [`docs/adr/0001-float-window-wpf.md`](docs/adr/0001-float-window-wpf.md)）
- 配置读写统一走 `ApplicationConfig`（key/value），默认值集中在 `App.InitializeDefaultSettings`
- [`CONTEXT.md`](CONTEXT.md)：术语与行为约定——显示时长从哪一刻起算、无线电消息怎么判定、
  坐标按什么形状识别、更新源为什么指向本 fork。改这些行为前建议先看它
- 打包 MSIX 需要 Windows App SDK 与 .NET 桌面工作负载；日常改浮窗样式可以只跑 Float 项目单独验证

## 已知取舍

- 无线电文案表只取了游戏 `lang/ui.csv` 的**英文 + 简体中文**两列，其他客户端语言靠文案前缀/后缀兜底；
  要扩语言按 `CONTEXT.md` 里记的数据源再取一遍即可
- 坐标是按"左括号 + 字母 + 数字"的形状识别的，玩家手打的类似方括号可能被染成坐标色；
  无线电文案同理，可能误判个别手打消息——**两者都只影响颜色，不影响文字内容**
- 自定义 AI 的延迟与质量取决于你接的模型，本地跑建议 7B 以上；Google 接口需要相应的网络环境；
  Bing 需要 Token，界面上暂未开放
- 浮窗历史消息在重装升级时会清空（进程重启）

## 许可与致谢

- 许可证：GPL v3（与上游一致），本修改版的全部改动都在本仓库的提交历史里
- 原项目作者：[IShiraiKurokoI](https://github.com/IShiraiKurokoI)
  （[BiliBili 主页](https://space.bilibili.com/310144483)），感谢原始开发
