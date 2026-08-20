# Third-Party Notices

WordStrip is licensed under the Apache License 2.0 (see `LICENSE`). It bundles,
depends on, or is built from the components below, each under its own terms.

---

## Bundled in the shipped application

### SymSpell frequency dictionary
**File:** `assets/dict/frequency_dictionary_en_82_765.txt`
**Source:** [SymSpell](https://github.com/wolfgarbe/SymSpell) by Wolf Garbe
**Licence:** MIT

The word list is embedded in the executable. The SymSpell *algorithm* is
reimplemented here rather than taken as a dependency; the data file is used
as published.

### N-gram language model
**Files:** `assets/ngram/ngram-2.txt`, `assets/ngram/ngram-3.txt`
**Licence:** Apache 2.0 (part of this project)

These are generated artefacts, not third-party content. They are statistics —
conditional probabilities over word pairs and triples — derived from:

- **Project Gutenberg** texts, which are in the public domain in the United
  States. The texts themselves are not redistributed; only counts derived from
  them. Regenerate with `tools/ngram/Fetch-Corpus.ps1`.
  Project Gutenberg is a registered trademark of the Project Gutenberg Literary
  Archive Foundation and this project is not affiliated with or endorsed by it.
- **SymSpell bigram data** (MIT), as above.

> Anyone redistributing a *modified* corpus build should check the licence of
> whatever they trained on. The pipeline does not enforce this for you.

---

## Runtime dependencies

| Component | Version | Licence |
|---|---|---|
| [Microsoft.ML.OnnxRuntime](https://github.com/microsoft/onnxruntime) | 1.20.1 | MIT |
| [Microsoft.ML.Tokenizers](https://github.com/dotnet/machinelearning) | 1.0.1 | MIT |
| .NET 8 runtime (self-contained in the installer) | 8.x | MIT |

---

## Optional, downloaded by the user — **not** bundled

### DistilGPT2 (ONNX, int8)
**Source:** [onnx-community/distilgpt2-ONNX](https://huggingface.co/onnx-community/distilgpt2-ONNX)
**Upstream model:** [distilbert/distilgpt2](https://huggingface.co/distilbert/distilgpt2)
**Licence:** Apache 2.0, from the upstream model
**Size:** approximately 227 MB

This model is **never shipped with WordStrip**. It is downloaded only when the
user explicitly asks for it in Settings, having first been shown its name,
publisher, licence, download size and memory cost.

The ONNX conversion republished by onnx-community carries no licence statement
of its own, so the upstream Apache 2.0 terms are what apply. That is stated here
rather than quietly assumed.

---

## Build and test only — not redistributed

| Component | Version | Licence |
|---|---|---|
| xunit | 2.5.3 | Apache 2.0 |
| xunit.runner.visualstudio | 2.5.3 | Apache 2.0 |
| Microsoft.NET.Test.Sdk | 17.8.0 | MIT |
| coverlet.collector | 6.0.0 | MIT |
| [Inno Setup](https://jrsoftware.org/isinfo.php) 6 | — | Inno Setup licence |
| Visual Studio Build Tools (MSVC, Windows SDK) | 2022 | Microsoft licence terms |

---

*If you believe something is missing or misattributed here, please open an issue.
Getting attribution right matters more than getting it quickly.*
