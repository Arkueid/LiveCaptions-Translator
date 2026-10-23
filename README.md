<div align="center">

<img src="src/LiveCaptions-Translator.ico" width="128" height="128" alt="LiveCaptions-Translator Icon"/>

# LiveCaptions Translator

<a href="https://trendshift.io/repositories/14278" target="_blank"><img src="https://trendshift.io/api/badge/repositories/14278" alt="SakiRinn%2FLiveCaptions-Translator | Trendshift" style="width: 250px; height: 55px;" width="250" height="55"/></a>

### *Real-time audio/speech translation tool based on Windows LiveCaptions*

[![Master Build](https://github.com/SakiRinn/LiveCaptions-Translator/actions/workflows/dotnet-build.yml/badge.svg?branch=master)](https://github.com/SakiRinn/LiveCaptions-Translator/actions/workflows/dotnet-build.yml)
[![GitHub Release](https://img.shields.io/github/v/release/SakiRinn/LiveCaptions-Translator?label=Latest&color=yellow)](https://github.com/SakiRinn/LiveCaptions-Translator/releases/latest)
[![Windows 11](https://img.shields.io/badge/platform-Windows11-blue?logo=windows11&style=&color=1E9BFA)](https://www.microsoft.com/en-us/software-download/windows11)
[![GitHub License](https://img.shields.io/github/license/SakiRinn/LiveCaptions-Translator)](https://github.com/SakiRinn/LiveCaptions-Translator/blob/master/LICENSE)
[![GitHub Stars](https://img.shields.io/github/stars/SakiRinn/LiveCaptions-Translator)](https://github.com/SakiRinn/LiveCaptions-Translator/stargazers)

**English** | [中文](README_zh-CN.md)

</div>

> 🍴 **This repository is a fork of [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator).**
>
> Installation, Windows LiveCaptions setup and API configuration are unchanged and still documented [upstream](https://github.com/SakiRinn/LiveCaptions-Translator/wiki).
> What this branch changes is **how captions are displayed** — see [Changes in this Fork](#changes-in-this-fork).
> Pre-built binaries are published **upstream only**; this fork is built from source (see [Building from Source](#building-from-source)).

## Overview

**✨ LiveCaptions Translator = Windows LiveCaptions + Translate API ✨**

This is a lightweight tool that seamlessly integrates translation APIs with Windows Live Captions, enabling real-time speech translation without requiring a Copilot+ PC.

Windows' built-in LiveCaptions is easy to use, uses few resources, and has extremely high recognition accuracy. If empowering it with the awesome translation capabilities of LLMs, you will get... possibly the best real-time translator available to date!

**🚀 Quick Start:** Grab a build from the [upstream releases](https://github.com/SakiRinn/LiveCaptions-Translator/releases) if you only want the original feature set — the overlay work described below lives in this fork, so build it from source ([instructions](#building-from-source)).

<div align="center">
  <img src="images/preview.png" alt="Preview of LiveCaptions Translator" width="90%" />
  <br>
  <em style="font-size:80%">Preview of LiveCaptions Translator</em>
  <br>
</div>

## Features

- **🔄 Seamless Integration**

  Automatically invokes Windows LiveCaptions without opening separate windows. Provides a unified experience for real-time audio/speech translation.

  After your first use, Windows LiveCaptions will be hidden by default. You can show it again in the settings.

  <div align="center">
    <img src="images/show_livecaptions.png" alt="LiveCaptions Show/Hide button" width="90%" />
    <br>
    <em style="font-size:80%">LiveCaptions Show/Hide button</em>
    <br>
  </div>

  By enabling the ***Include microphone audio*** option in the setting of Windows LiveCaptions, you can achieve real-time speech translation!
  > ⚠️ **IMPORTANT:** You must change the source language in Windows LiveCaptions!

- **🎨 Modern Interface**

  Easy-to-use and clean Fluent UI aligned with modern Windows aesthetics.

  It can automatically switches between light and dark themes 🌓 based on the system setting.

- **🌐 Multiple Translation Services**

  Supports various translation engines, including 2 out-of-the-box Google Translate.

  Implemented translation engines are shown in the table below:

  <div align="center">

  | API                                                 | Type        | Hosting     |
  |-----------------------------------------------------|-------------|-------------|
  | [Ollama](https://ollama.com)                        | LLM-based   | Self-hosted |
  | OpenAI Compatible API                               | LLM-based   | Online      |
  | [OpenRouter](https://openrouter.ai)                 | LLM-based   | Online      |
  | Google Translate                                    | Traditional | Online      |
  | DeepL                                               | Traditional | Online      |
  | Youdao                                              | Traditional | Online      |
  | Baidu Translate                                     | Traditional | Online      |
  | [MTranServer](https://github.com/xxnuo/MTranServer) | Traditional | Self-hosted |
  | [LibreTranslate](https://libretranslate.com/)       | Traditional | Self-hosted |

  </div>

  It's strongly recommended using **LLM-based** translation engines, as LLMs excel at handling incomplete sentences and are adept at understanding context.

- **🪟 Overlay Window**

  The overlay is the only place captions are drawn: the main window is settings and history only, and the overlay opens with the app. Borderless and transparent, it provides the most immersive experience — very useful for gaming, videos, and live streams!

  You can even make it completely embedded into the screen, becoming part of it. This means it won't affect any of your operations at all! This is perfect for gamers.

  <div align="center">
    <img src="images/overlay_window.png" alt="Overlay Window" width="80%" />
    <br>
    <em style="font-size:80%">Overlay window</em>
    <br>
  </div>

  Hovering the overlay reveals a small toolbar that controls it in place: pause/resume, font size, bold, outline, text and background colors, background opacity, what to show (both / translation only / source only), line order, and click-through. It fades away as soon as the pointer leaves, so it never covers the text unless it is wanted.

  You can adjust the number of sentences displayed simultaneously in the *Overlay Sentences* section of the setting page.

- **🧷 Captions That Do Not Rewind**

  Windows LiveCaptions keeps rewriting the sentence it is still hearing — words appear, get rephrased, and shift around. Instead of painting that raw stream, the text is committed in stable chunks: what you have already read never disappears or moves, and only the still-changing tail waits (about 0.2 s of silence) before it is shown.

  Every finished sentence also gets its own identity, so a translation is always attached to the sentence it belongs to. That is what removed the duplicated and skipped sentence groups of the old display.

- **⏱️ Latency Readout**

  With *Show Latency* enabled, each translation is prefixed with how long it took, plus the model-side prefill and decode split (e.g. `1420 ms (380+1040)`), which makes it obvious whether an API or a local model is the bottleneck.

- **⚙️ Flexible Controls**

  Always-on-top window, and pause/resume right on the overlay's own toolbar — pausing stops API calls and clears the screen, so nothing stale is left behind.

- **📒 History Management**

  Records original and translated text, perfect for meetings, lectures, and important discussions.

  You can export all records as a CSV file.

  <div align="center">
    <img src="images/history.png" alt="Translation history" width="90%" />
    <br>
    <em style="font-size:80%">Translation history</em>
    <br>
  </div>

## Changes in this Fork

The recognised-text pipeline was rebuilt. The list below is what that means in use:

- **Monotonic subtitle text.** A transcript buffer (`TranscriptStabilizer`) commits a prefix and only ever appends to it; the tail is held back for a few characters and flushed after ~0.2 s of silence. Text that has already been painted is never deleted and re-printed, which was the "letters disappear and come back" flicker.
- **One identity per sentence.** A timeline assigns every finished sentence a sequence number; a translation is looked up by that number instead of by fuzzy string matching. Duplicate and swallowed sentence groups came from that guessing.
- **Finished sentences go first.** Only one request is in flight at a time (local models degrade badly with parallel requests). When a sentence finishes, a speculative translation of the still-growing sentence is cancelled so the GPU starts on text the reader will keep.
- **Single snapshot, single paint.** The overlay draws one immutable snapshot per update instead of a handful of properties written from different threads — every intermediate state used to be a picture it could paint.
- **Overlay owns its controls.** Pause, typography, colors, opacity, show mode, order and click-through all live in a translucent toolbar that fades in on hover; `Ctrl+Alt+T` restores input after click-through is enabled.
- **Ollama fixes.** Requests send `temperature` and `num_predict` instead of the ignored `max_tokens`, and thinking is disabled for Qwen 3.5 (it was burning the whole output budget on reasoning).

## Building from Source

This fork does not publish binaries, so build it:

```bash
git clone https://github.com/Arkueid/LiveCaptions-Translator.git
cd LiveCaptions-Translator
dotnet build -c Release
```

The output lands in `bin/Release/net8.0-windows/` — start `LiveCaptionsTranslator.exe` from there. Building needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0); the runtime mentioned below is enough to *run* a build, not to make one.

## Prerequisites

<div align="center">

| Requirement                                                                                                           | Details                                     |
|-----------------------------------------------------------------------------------------------------------------------|---------------------------------------------|
| <img src="https://img.shields.io/badge/Windows-11%20(22H2+)-0078D6?style=for-the-badge&logo=windows&logoColor=white"> | With LiveCaptions support.                  |
| <img src="https://img.shields.io/badge/.NET-8.0+-512BD4?style=for-the-badge&logo=dotnet&logoColor=white">             | Recommended. Not test in previous versions. |

</div>

This tool is based on Windows LiveCaptions, which is available since **Windows 11 22H2**.

We suggest you have **.NET runtime 8.0** or higher installed. If you are not available to install one, you can download the ***with runtime*** version but its size is bigger.

<div align="center">
  <p align="center">
    <a href="https://github.com/SakiRinn/LiveCaptions-Translator/wiki">
      <img src="https://img.shields.io/badge/📚_Check_our_Wiki_for_detailed_information-2ea44f?style=for-the-badge" alt="Check our Wiki">
    </a>
  </p>
</div>

## Getting Started

> ⚠️ **IMPORTANT:** You must complete the following steps before running LiveCaptions Translator for the first time.
>
> For detailed information, see Microsoft's guide on [Using live captions](https://support.microsoft.com/en-us/windows/use-live-captions-to-better-understand-audio-b52da59c-14b8-4031-aeeb-f6a47e6055df).

### Step 1: Verify Windows LiveCaptions Availability

Confirm LiveCaptions is available on your system using any of these methods:

- Toggle **Live captions** in the quick settings
- Press **Win + Ctrl + L**
- Access via **Quick settings** > **Accessibility** > **Live captions**
- Open **Start** > **All apps** > **Accessibility** > **Live captions**
- Navigate to **Settings** > **Accessibility** > **Captions** and enable **Live captions**

### Step 2: Configure LiveCaptions

When you first start, Windows LiveCaptions will ask for your consent to process voice data on your device and prompt you to download language files to be used by on-device speech recognition.

After launching Windows LiveCaptions, click the **⚙️ gear** icon to open the setting menu, then select **Position** > **Overlaid on screen**.

> ⚠️ **VERY IMPORTANT!** Otherwise, a display bug will occur on the screen after hiding Windows LiveCaptions.

<div align="center">
  <img src="images/speech_recognition.png" alt="Items under speech recognition" width="80%" />
  <br>
  <em style="font-size:80%">Required speech recognition downloads</em>
  <br>
</div>

After configuration, close Windows LiveCaptions and launch LiveCaptions Translator to start using it! 🎉

## Project Stats

### Activity

<div align="center">
  <img src="https://img.shields.io/github/issues/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Issues&color=yellow" alt="GitHub Issues">
  <img src="https://img.shields.io/github/issues-pr/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Pull%20Requests&color=blue" alt="GitHub Pull Requests">
  <img src="https://img.shields.io/github/discussions/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Discussions&color=orange" alt="GitHub Discussions">
  <img src="https://img.shields.io/github/last-commit/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Last%20Commit&color=purple" alt="GitHub Last Commit">
</div>

### Contributors

<div align="center">
  <img src="https://img.shields.io/github/contributors/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Contributors&color=success" alt="GitHub Contributors">
  <br>
  <a href="https://github.com/SakiRinn/LiveCaptions-Translator/graphs/contributors">
    <img src="https://contrib.rocks/image?repo=SakiRinn/LiveCaptions-Translator" />
  </a>
</div>

### Star History

[![Stargazers over time](https://starchart.cc/SakiRinn/LiveCaptions-Translator.svg?variant=adaptive)](https://starchart.cc/SakiRinn/LiveCaptions-Translator)
