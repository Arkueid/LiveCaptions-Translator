<div align="center">

<img src="src/LiveCaptions-Translator.ico" width="128" height="128" alt="LiveCaptions Translator图标"/>

# LiveCaptions Translator

<a href="https://trendshift.io/repositories/14278" target="_blank"><img src="https://trendshift.io/api/badge/repositories/14278" alt="SakiRinn%2FLiveCaptions-Translator | Trendshift" style="width: 250px; height: 55px;" width="250" height="55"/></a>

### *基于Windows实时字幕的实时音频/语音翻译工具*

[![Master Build](https://github.com/SakiRinn/LiveCaptions-Translator/actions/workflows/dotnet-build.yml/badge.svg?branch=master)](https://github.com/SakiRinn/LiveCaptions-Translator/actions/workflows/dotnet-build.yml)
[![GitHub Release](https://img.shields.io/github/v/release/SakiRinn/LiveCaptions-Translator?label=Latest&color=yellow)](https://github.com/SakiRinn/LiveCaptions-Translator/releases/latest)
[![Windows 11](https://img.shields.io/badge/platform-Windows11-blue?logo=windows11&style=&color=1E9BFA)](https://www.microsoft.com/en-us/software-download/windows11)
[![GitHub License](https://img.shields.io/github/license/SakiRinn/LiveCaptions-Translator)](https://github.com/SakiRinn/LiveCaptions-Translator/blob/master/LICENSE)
[![GitHub Stars](https://img.shields.io/github/stars/SakiRinn/LiveCaptions-Translator)](https://github.com/SakiRinn/LiveCaptions-Translator/stargazers)

[English](README.md) | **中文**

</div>

> 🍴 **本仓库是 [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 的 fork。**
>
> 安装方式、Windows 实时字幕的配置和翻译 API 的设置都没有变化，仍以上游[文档](https://github.com/SakiRinn/LiveCaptions-Translator/wiki)为准。
> 本分支改的是**字幕的显示方式**，详见[本分支的改动](#本分支的改动)。
> 预编译版本仅在上游发布；本 fork 需要[从源码构建](#从源码构建)。

## 概述

**✨ LiveCaptions Translator = Windows实时字幕 + 翻译API ✨**

这是一个无缝集成翻译API与Windows实时字幕的轻量级工具。它可以实现实时语音翻译，无需Copilot+ PC。

Windows内置的实时字幕简单易用，资源占用少，且识别准确率极高。如果为其赋能LLM强大的翻译能力，你将获得...可能是目前最好的实时翻译器！

**🚀 快速开始:** 只想要原有功能，可以从[上游发布页面](https://github.com/SakiRinn/LiveCaptions-Translator/releases)下载；下文所述的悬浮窗改动都在本 fork 中，请[从源码构建](#从源码构建)。

<div align="center">
  <img src="images/preview.png" alt="LiveCaptions Translator预览" width="90%" />
  <br>
  <em style="font-size:80%">LiveCaptions Translator预览</em>
  <br>
</div>

## 功能特性

- **🔄 无缝集成**

  自动调用Windows实时字幕而无需打开单独窗口。为实时音频/语音翻译提供统一体验。

  首次使用后，Windows实时字幕默认将被隐藏。您可以在设置中再次显示它。

  <div align="center">
    <img src="images/show_livecaptions.png" alt="实时字幕显示/隐藏按钮" width="90%" />
    <br>
    <em style="font-size:80%">实时字幕显示/隐藏按钮</em>
    <br>
  </div>

  通过在Windows实时字幕设置中启用 ***包含麦克风音频*** 选项，您可以实现实时语音翻译！
  > ⚠️ **重要:** 您必须在Windows实时字幕中更改源语言！

- **🎨 现代化界面**

  易于使用且简洁的Fluent UI与现代Windows美学保持一致。

  它可以根据系统设置自动在浅色和深色主题🌓之间切换。

- **🌐 多种翻译服务**

  支持各种翻译引擎，包括2个开箱即用的谷歌翻译。

  已实现的翻译引擎如下表所示：

  <div align="center">

  | API                                                 | 类型    | 托管方式 |
  |-----------------------------------------------------|-------|------|
  | [Ollama](https://ollama.com)                        | 基于LLM | 自托管  |
  | OpenAI兼容API                                         | 基于LLM | 在线   |
  | [OpenRouter](https://openrouter.ai)                 | 基于LLM | 在线   |
  | 谷歌翻译                                                | 传统翻译  | 在线   |
  | DeepL                                               | 传统翻译  | 在线   |
  | 有道翻译                                                | 传统翻译  | 在线   |
  | 百度翻译                                                | 传统翻译  | 在线   |
  | [MTranServer](https://github.com/xxnuo/MTranServer) | 传统翻译  | 自托管  |
  | [LibreTranslate](https://libretranslate.com/)       | 传统翻译  | 自托管  |

  </div>

  强烈推荐使用 **基于LLM** 的翻译引擎，因为LLM擅长处理不完整的句子并能很好地理解上下文。

- **🪟 悬浮窗口**

  字幕只在悬浮窗口中绘制：主窗口只负责设置与历史，悬浮窗随程序启动自动打开。无边框、透明，提供最沉浸式的体验，对游戏、视频和直播等场景非常有用！

  您甚至可以使其完全嵌入到屏幕中，成为屏幕的一部分。这意味着它不会影响您的任何操作！这对游戏玩家来说再合适不过了。

  <div align="center">
    <img src="images/overlay_window.png" alt="悬浮窗口" width="80%" />
    <br>
    <em style="font-size:80%">悬浮窗口</em>
    <br>
  </div>

  鼠标移入悬浮窗时会浮出一条小工具条，就地调整：暂停/恢复、字号、加粗、描边、文字与背景颜色、背景不透明度、显示内容（双行 / 仅译文 / 仅原文）、上下顺序，以及鼠标穿透。指针离开即淡出，因此不会遮挡字幕。

  您可以在设置页的 *Overlay Sentences* 选项调整同时显示的句子数量。

- **🧷 不会回退的字幕**

  Windows 实时字幕会不断改写它正在听的句子：字先出现、被改写、又挪位置。本分支不再直接绘制这串原始数据，而是把文本按稳定片段提交：已经读到的字不会消失也不会移动，只有仍在变化的尾巴会等待约 0.2 秒的静默再上屏。

  每个完结的句子还会获得独立编号，译文始终挂在自己所属的句子上——这正是旧版本里"句组重复、句组被吞"的根源。

- **⏱️ 延迟读数**

  打开 *Show Latency* 后，每条译文前会显示耗时以及模型侧的 prefill / decode 拆分（如 `1420 ms (380+1040)`），一眼看出瓶颈在 API 还是在本地模型。

- **⚙️ 灵活控制**

  支持窗口置顶，暂停/恢复则放在悬浮窗自己的工具条上——暂停会停止 API 调用并清空画面，不会留下过期内容。

- **📒 历史记录管理**

  记录原文和翻译文本，非常适合会议、讲座和重要讨论。

  您可以将所有记录导出为CSV文件。

  <div align="center">
    <img src="images/history.png" alt="翻译历史" width="90%" />
    <br>
    <em style="font-size:80%">翻译历史</em>
    <br>
  </div>

## 本分支的改动

识别文本的处理流程被重写过，用起来是这样的：

- **单调的字幕文本。** 转录缓冲（`TranscriptStabilizer`）只提交前缀并向后追加，尾部会扣留几个字符，静默约 0.2 秒后才上屏。已经画出来的字不会再被删掉重打——这就是之前"字闪一下又吐出来"的来源。
- **一句一个身份。** 时间线给每个完结句子分配序号，译文按序号查找，而不是靠模糊字符串匹配猜。重复和丢句都来自那次猜测。
- **完结句优先。** 同时只允许一个请求在飞（本地模型并行会明显退化）。句子完结时，对仍在生长的句子发出的推测性翻译会被取消，让 GPU 先算会被保留的文本。
- **单一快照、单次绘制。** 每次更新悬浮窗只绘制一个不可变快照，取代原先"多个属性分头写"的做法——那时每个中间状态都是它能画出来的一帧。
- **控制权在悬浮窗。** 暂停、字体、颜色、透明度、显示模式、顺序、鼠标穿透都在一条悬停淡入的半透明工具条里；开启穿透后按 `Ctrl+Alt+T` 恢复输入。
- **Ollama 修正。** 请求改用 `temperature` 与 `num_predict`（`max_tokens` 不被识别），并且对 Qwen 3.5 关闭了 thinking（它会把输出预算全烧在推理上）。

## 从源码构建

本 fork 不发布预编译包，请自行构建：

```bash
git clone https://github.com/Arkueid/LiveCaptions-Translator.git
cd LiveCaptions-Translator
dotnet build -c Release
```

产物在 `bin/Release/net8.0-windows/`，启动其中的 `LiveCaptionsTranslator.exe` 即可。构建需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)；下文提到的运行时只够*运行*构建产物，不能用来构建。

## 系统要求

<div align="center">

| 要求                                                                                                                    | 详情          |
|-----------------------------------------------------------------------------------------------------------------------|-------------|
| <img src="https://img.shields.io/badge/Windows-11%20(22H2+)-0078D6?style=for-the-badge&logo=windows&logoColor=white"> | 支持实时字幕功能    |
| <img src="https://img.shields.io/badge/.NET-8.0+-512BD4?style=for-the-badge&logo=dotnet&logoColor=white">             | 推荐。未在之前版本测试 |

</div>

本工具基于Windows实时字幕，该功能自 **Windows 11 22H2** 起可用。

我们建议您安装 **.NET运行时8.0** 或更高版本。如果您无法安装，可以下载 ***with runtime*** 版本，但其文件较大。

<div align="center">
  <p align="center">
    <a href="https://github.com/SakiRinn/LiveCaptions-Translator/wiki">
      <img src="https://img.shields.io/badge/📚_查看我们的Wiki获取详细信息-2ea44f?style=for-the-badge" alt="查看我们的Wiki">
    </a>
  </p>
</div>

## 入门指南

> ⚠️ **重要:** 首次运行LiveCaptions Translator前，您必须完成以下步骤。
>
> 有关详细信息，请参阅Microsoft的[使用实时字幕](https://support.microsoft.com/zh-cn/windows/使用实时字幕更好地理解音频-b52da59c-14b8-4031-aeeb-f6a47e6055df)指南。

### 步骤1: 验证Windows实时字幕可用性

使用以下任一方法确认您的系统上可用实时字幕：

- 在快速设置中切换 **实时字幕**
- 按 **Win + Ctrl + L**
- 通过 **快速设置** > **辅助功能** > **实时字幕** 访问
- 打开 **开始** > **所有应用** > **辅助功能** > **实时字幕**
- 导航至 **设置** > **辅助功能** > **字幕** 并启用 **实时字幕**

### 步骤2: 配置Windows实时字幕

首次启动时，Windows实时字幕会请求您同意在设备上处理语音数据，并提示您下载用于设备上语音识别的语言文件。

启动Windows实时字幕后，点击 **⚙️齿轮** 图标打开设置菜单，然后选择 **位置** > **覆盖在屏幕上** 。

> ⚠️ **非常重要！** 否则隐藏Windows实时字幕后屏幕会出现显示BUG.

<div align="center">
  <img src="images/speech_recognition.png" alt="语音识别下的项目" width="80%" />
  <br>
  <em style="font-size:80%">需要下载的语音识别组件</em>
  <br>
</div>

配置完成后，关闭Windows实时字幕然后开始使用LiveCaptions Translator吧！🎉

## 项目统计

### 活动

<div align="center">
  <img src="https://img.shields.io/github/issues/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Issues&color=yellow" alt="GitHub Issues">
  <img src="https://img.shields.io/github/issues-pr/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Pull%20Requests&color=blue" alt="GitHub Pull Requests">
  <img src="https://img.shields.io/github/discussions/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Discussions&color=orange" alt="GitHub Discussions">
  <img src="https://img.shields.io/github/last-commit/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Last%20Commit&color=purple" alt="GitHub Last Commit">
</div>

### 贡献者

<div align="center">
  <img src="https://img.shields.io/github/contributors/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Contributors&color=success" alt="GitHub Contributors">
  <br>
  <a href="https://github.com/SakiRinn/LiveCaptions-Translator/graphs/contributors">
    <img src="https://contrib.rocks/image?repo=SakiRinn/LiveCaptions-Translator" />
  </a>
</div>

### Star历史

[![Stargazers over time](https://starchart.cc/SakiRinn/LiveCaptions-Translator.svg?variant=adaptive)](https://starchart.cc/SakiRinn/LiveCaptions-Translator)
