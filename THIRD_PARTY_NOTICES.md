# Third-party notices

Hesper itself is released under the [MIT License](LICENSE). It builds on the projects below,
which keep their own licenses.

- **Mac app:** only `uv` ships inside it.
- **Windows app and Linux AppImage:** they also carry the .NET runtime and, on Windows, the
  WebView2 SDK and NAudio.
- **Everything else** is downloaded on first setup (the engine) or when a model is chosen (the AI
  models), from its own publisher, with its own license.

The full license texts of what ships inside the apps are in
[THIRD_PARTY_LICENSES.txt](THIRD_PARTY_LICENSES.txt), which each app carries: in the Mac app's
Resources and the DMG's Licenses folder; in Windows and Linux, Settings → About → Third-party
licenses.

## AI models

| Model | Used for | License | Source |
|---|---|---|---|
| OpenAI Whisper large-v3-turbo, large-v3 | speech to text | MIT | [openai/whisper](https://github.com/openai/whisper). Mac: [mlx-community/whisper-large-v3-turbo](https://huggingface.co/mlx-community/whisper-large-v3-turbo). Windows and Linux: [mobiuslabsgmbh/faster-whisper-large-v3-turbo](https://huggingface.co/mobiuslabsgmbh/faster-whisper-large-v3-turbo), [Systran/faster-whisper-large-v3](https://huggingface.co/Systran/faster-whisper-large-v3) |
| Qwen3.5 4B / 9B, Qwen3 4B Instruct 2507 / 8B | notes, answers | Apache 2.0 | [Qwen](https://huggingface.co/Qwen). Mac: MLX conversions by [mlx-community](https://huggingface.co/mlx-community). Windows and Linux: GGUF conversions by [unsloth](https://huggingface.co/unsloth) |
| pyannote segmentation 3.0 (ONNX) | speaker separation | MIT | [pyannote/segmentation-3.0](https://huggingface.co/pyannote/segmentation-3.0), via [sherpa-onnx models](https://github.com/k2-fsa/sherpa-onnx/releases) |
| NVIDIA NeMo TitaNet small (ONNX) | speaker voiceprints | CC BY 4.0 | [nvidia/speakerverification_en_titanet_small](https://huggingface.co/nvidia/speakerverification_en_titanet_small), via [sherpa-onnx models](https://github.com/k2-fsa/sherpa-onnx/releases) |

Custom models chosen in Settings → AI models come with their own licenses (see their Hugging Face
page).

## Software

### The engine, on every computer

| Project | License | Source |
|---|---|---|
| uv | MIT or Apache 2.0 | [astral-sh/uv](https://github.com/astral-sh/uv) |
| Python (installed by uv) | PSF License | [python.org](https://docs.python.org/3/license.html) |
| sherpa-onnx | Apache 2.0 | [k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) |
| ONNX Runtime | MIT | [microsoft/onnxruntime](https://github.com/microsoft/onnxruntime) |
| Hugging Face Hub, Transformers, tokenizers | Apache 2.0 | [huggingface](https://github.com/huggingface) |
| NumPy | BSD 3-Clause | [numpy/numpy](https://github.com/numpy/numpy) |
| soundfile / libsndfile | BSD 3-Clause / LGPL 2.1 | [bastibe/python-soundfile](https://github.com/bastibe/python-soundfile), [libsndfile](https://github.com/libsndfile/libsndfile) |
| icalendar | BSD 2-Clause | [collective/icalendar](https://github.com/collective/icalendar) |
| recurring-ical-events | LGPL 3.0 or later | [niccokunzmann/python-recurring-ical-events](https://github.com/niccokunzmann/python-recurring-ical-events) |
| truststore | MIT | [sethmlarson/truststore](https://github.com/sethmlarson/truststore) |

### Mac

| Project | License | Source |
|---|---|---|
| MLX | MIT, © Apple Inc. | [ml-explore/mlx](https://github.com/ml-explore/mlx) |
| mlx-lm (the notes model) | MIT, © Apple Inc. | [ml-explore/mlx-lm](https://github.com/ml-explore/mlx-lm) |
| mlx-whisper (speech to text) | MIT, © Apple Inc. | [ml-explore/mlx-examples](https://github.com/ml-explore/mlx-examples/tree/main/whisper) |

### Windows and Linux

| Project | License | Source |
|---|---|---|
| faster-whisper (speech to text) | MIT | [SYSTRAN/faster-whisper](https://github.com/SYSTRAN/faster-whisper) |
| CTranslate2 | MIT | [OpenNMT/CTranslate2](https://github.com/OpenNMT/CTranslate2) |
| PyAV, with FFmpeg inside its packages | BSD 3-Clause; FFmpeg: (L)GPL | [PyAV-Org/PyAV](https://github.com/PyAV-Org/PyAV), [FFmpeg's licensing](https://ffmpeg.org/legal.html) |
| llama.cpp (the notes model) | MIT | [ggml-org/llama.cpp](https://github.com/ggml-org/llama.cpp) |
| NVIDIA cuBLAS and cuDNN (only on a PC with an NVIDIA graphics card) | NVIDIA's license | [NVIDIA CUDA](https://developer.nvidia.com/cuda-zone), downloaded from PyPI under [NVIDIA's terms](https://docs.nvidia.com/cuda/eula/index.html) |
| .NET runtime (in the Windows app and the Linux AppImage) | MIT | [dotnet/runtime](https://github.com/dotnet/runtime) |
| WebView2 SDK (in the Windows app) | BSD-style, © Microsoft | [Microsoft Edge WebView2](https://aka.ms/webview) |
| NAudio (in the Windows app) | MIT | [naudio/NAudio](https://github.com/naudio/NAudio) |
| AppImage runtime (in the Linux AppImage) | MIT | [AppImage/type2-runtime](https://github.com/AppImage/type2-runtime) |

On Linux, Hesper uses the desktop's own GTK, WebKitGTK, PulseAudio or PipeWire, libnotify and
AppIndicator libraries; it doesn't ship them.

The complete list of Python packages and versions is in [`uv.lock`](uv.lock). Each package's
license file is installed next to it in the engine's folder:
- Mac: `~/Library/Application Support/Hesper/venv`
- Windows: `%LOCALAPPDATA%\Hesper\venv`
- Linux: `~/.local/share/Hesper/venv`

The full license texts are at the links above. CC BY 4.0 attribution: TitaNet © NVIDIA
Corporation, used unmodified (converted to ONNX by the sherpa-onnx project).
