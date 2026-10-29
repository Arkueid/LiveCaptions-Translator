<div align="center">

<img src="src/LiveCaptions-Translator.ico" width="96" height="96" alt="LiveCaptions Translator图标"/>

# LiveCaptions Translator — Fork Edition

**基于 Windows 实时字幕的实时语音翻译，重写了字幕显示管线。**

[![Windows 11](https://img.shields.io/badge/platform-Windows11-blue?logo=windows11&style=&color=1E9BFA)](https://www.microsoft.com/en-us/software-download/windows11)
[![GitHub License](https://img.shields.io/github/license/Arkueid/LiveCaptions-Translator)](https://github.com/Arkueid/LiveCaptions-Translator/blob/master/LICENSE)

[English](README.md) | **中文**

</div>

本仓库是 **[SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator)** 的 fork——原项目、[发布页面](https://github.com/SakiRinn/LiveCaptions-Translator/releases)和[上游 Wiki](https://github.com/SakiRinn/LiveCaptions-Translator/wiki) 都在那里。Windows 实时字幕的配置方式、支持的翻译 API（Ollama、OpenAI 兼容、OpenRouter、Google、DeepL、有道、百度、MTranServer、LibreTranslate）全部原样继承。

这里不一样的是**字幕的显示与控制方式**，也就是本 README 要讲的内容。

## 预览

字幕在一个随程序启动的透明悬浮窗里绘制：原文行调暗、译文行更亮：

<p align="center">
  <img src="images/fork_captions.png" alt="悬浮字幕：原文暗淡、译文明亮" width="80%" />
</p>

应用启动后只有悬浮窗（首次使用时设置窗口会一并出现）。鼠标悬停悬浮窗会淡入一条紧凑的工具条——暂停、字号、加粗、描边、文字颜色、背景不透明度与颜色、显示内容、行序、鼠标穿透，以及一个打开设置窗口的齿轮：

<p align="center">
  <img src="images/fork_toolbar.png" alt="带悬停工具条的悬浮窗" width="80%" />
  <br>
  <img src="images/fork_colors.png" alt="自定义文字颜色" width="80%" />
</p>

> 背景是示意图；悬浮窗本体（底框、文字、工具条）由真实 UI 代码渲染。

## 本分支改了什么

- **字幕不会回退。** Windows 实时字幕会不断改写它正在听的句子。这里不再直接绘制原始数据流，而是由稳定器单调地提交文本：已经读到的字不会消失、不会移动，只有仍在变化的尾巴等约 0.2 秒静默后再上屏。
- **一句一个身份。** 每个完结句子都有序号，译文按序号挂靠，而不是靠模糊字符串匹配去猜——旧版"句组重复、句组被吞"就是这么来的。
- **完结句优先。** 同时只有一个翻译请求在飞（本地模型并行会明显退化）。句子完结时，对仍在生长句子的推测性翻译会被取消，让模型先算读者真正会保留的文本。
- **一帧一个快照。** 每次更新悬浮窗只绘制一个不可变快照，取代原先"多个显示属性被不同线程接连改写"的做法。
- **字幕都在悬浮窗上。** 悬浮窗始终置顶显示；暂停、字号、加粗、描边、文字/背景颜色、不透明度、显示内容（双行 / 仅译文 / 仅原文）、行序、鼠标穿透，以及打开设置窗口的齿轮都在悬停工具条里；穿透后按 `Ctrl+Alt+T` 恢复输入。样式按钮立即生效。
- **延迟读数。** 打开 *Show Latency* 后，每条译文前会显示耗时和模型侧 prefill/decode 拆分（如 `1420 ms (380+1040)`）。
- **Ollama 修正。** 请求改用 `temperature` / `num_predict`（之前发送的 `max_tokens` 不被识别），并对 Qwen 3.5 关闭 thinking。

### 相比上游移除的部分

- 关闭设置窗口不再退出程序——只是隐藏，悬浮窗工具条的齿轮可以随时把它带回来。退出走两个窗口标题栏的电源按钮，或直接关闭悬浮窗。
- 字幕页和日志卡片——字幕只在悬浮窗显示。
- 标题栏的暂停按钮——暂停在悬浮窗工具条上。
- 并行/排队的翻译任务队列——改为单请求在飞 + 完结句抢占。

## 从源码构建

本 fork 不发布预编译包：

```bash
git clone https://github.com/Arkueid/LiveCaptions-Translator.git
cd LiveCaptions-Translator
dotnet build -c Release
```

运行 `bin/Release/net8.0-windows/LiveCaptionsTranslator.exe`。构建需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

## 首次使用

与上游一致：确认 Windows 实时字幕可用（`Win + Ctrl + L`），把它的 **位置** 设为 *重叠在屏幕上*，在设置页选择 API 与目标语言——细节见[上游 Wiki](https://github.com/SakiRinn/LiveCaptions-Translator/wiki)。

## 致谢与许可

- 原项目与所有基础工作：[@SakiRinn](https://github.com/SakiRinn) 及[贡献者](https://github.com/SakiRinn/LiveCaptions-Translator/graphs/contributors)。
- 本 fork 由 [@Arkueid](https://github.com/Arkueid) 维护。
- 许可证与上游相同：[Apache License 2.0](LICENSE)。
