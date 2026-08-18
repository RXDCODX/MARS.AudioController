using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace MARS.AudioController.Services.TTS;

// Available languages for multilingual TTS
public static class TtsLanguages
{
    public static readonly string[] Available =
    [
        "en",
        "ko",
        "ja",
        "ar",
        "bg",
        "cs",
        "da",
        "de",
        "el",
        "es",
        "et",
        "fi",
        "fr",
        "hi",
        "hr",
        "hu",
        "id",
        "it",
        "lt",
        "lv",
        "nl",
        "pl",
        "pt",
        "ro",
        "ru",
        "sk",
        "sl",
        "sv",
        "tr",
        "uk",
        "vi",
        "na",
    ];
}

// ============================================================================
// Configuration classes
// ============================================================================

public class Config
{
    public AeConfig Ae { get; set; } = null!;
    public TTLConfig TTL { get; set; } = null!;

    public class AeConfig
    {
        public int SampleRate { get; set; }
        public int BaseChunkSize { get; set; }
    }

    public class TTLConfig
    {
        public int ChunkCompressFactor { get; set; }
        public int LatentDim { get; set; }
    }
}

// ============================================================================
// Style class
// ============================================================================
public class Style(float[] ttl, long[] ttlShape, float[] dp, long[] dpShape)
{
    public float[] Ttl { get; set; } = ttl;
    public long[] TtlShape { get; set; } = ttlShape;
    public float[] Dp { get; set; } = dp;
    public long[] DpShape { get; set; } = dpShape;
}

// ============================================================================
// Unicode text processor
// ============================================================================

public class UnicodeProcessor
{
    private readonly Dictionary<int, long> _indexer;

    public UnicodeProcessor(string unicodeIndexerPath)
    {
        var json = File.ReadAllText(unicodeIndexerPath);
        var indexerArray =
            JsonSerializer.Deserialize<long[]>(json)
            ?? throw new Exception("Failed to load indexer");
        _indexer = new Dictionary<int, long>();
        for (var i = 0; i < indexerArray.Length; i++)
        {
            _indexer[i] = indexerArray[i];
        }
    }

    private static string RemoveEmojis(string text)
    {
        var result = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            int codePoint;
            if (
                char.IsHighSurrogate(text[i])
                && i + 1 < text.Length
                && char.IsLowSurrogate(text[i + 1])
            )
            {
                // Get the full code point from surrogate pair
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++; // Skip the low surrogate
            }
            else
            {
                codePoint = text[i];
            }

            // Check if code point is in emoji ranges
            var isEmoji =
                codePoint is >= 0x1F600 and <= 0x1F64F
                || codePoint is >= 0x1F300 and <= 0x1F5FF
                || codePoint is >= 0x1F680 and <= 0x1F6FF
                || codePoint is >= 0x1F700 and <= 0x1F77F
                || codePoint is >= 0x1F780 and <= 0x1F7FF
                || codePoint is >= 0x1F800 and <= 0x1F8FF
                || codePoint is >= 0x1F900 and <= 0x1F9FF
                || codePoint is >= 0x1FA00 and <= 0x1FA6F
                || codePoint is >= 0x1FA70 and <= 0x1FAFF
                || codePoint is >= 0x2600 and <= 0x26FF
                || codePoint is >= 0x2700 and <= 0x27BF
                || codePoint is >= 0x1F1E6 and <= 0x1F1FF;

            if (!isEmoji)
            {
                if (codePoint > 0xFFFF)
                {
                    // Add back as surrogate pair
                    result.Append(char.ConvertFromUtf32(codePoint));
                }
                else
                {
                    result.Append((char)codePoint);
                }
            }
        }
        return result.ToString();
    }

    private string PreprocessText(string text, string lang)
    {
        // TODO: Need advanced normalizer for better performance
        text = text.Normalize(NormalizationForm.FormKD);

        // Remove emojis (wide Unicode range)
        // C# doesn't support \u{...} syntax in regex, so we use character filtering instead
        text = RemoveEmojis(text);

        // Replace various dashes and symbols
        var replacements = new Dictionary<string, string>
        {
            { "–", "-" }, // en dash
            { "‑", "-" }, // non-breaking hyphen
            { "—", "-" }, // em dash
            { "_", " " }, // underscore
            { "\u201C", "\"" }, // left double quote
            { "\u201D", "\"" }, // right double quote
            { "\u2018", "'" }, // left single quote
            { "\u2019", "'" }, // right single quote
            { "´", "'" }, // acute accent
            { "`", "'" }, // grave accent
            { "[", " " }, // left bracket
            { "]", " " }, // right bracket
            { "|", " " }, // vertical bar
            { "/", " " }, // slash
            { "#", " " }, // hash
            { "→", " " }, // right arrow
            { "←", " " }, // left arrow
        };

        foreach (var kvp in replacements)
        {
            text = text.Replace(kvp.Key, kvp.Value);
        }

        // Remove special symbols
        text = Regex.Replace(text, @"[♥☆♡©\\]", "");

        // Replace known expressions
        var exprReplacements = new Dictionary<string, string>
        {
            { "@", " at " },
            { "e.g.,", "for example, " },
            { "i.e.,", "that is, " },
        };

        foreach (var kvp in exprReplacements)
        {
            text = text.Replace(kvp.Key, kvp.Value);
        }

        // Fix spacing around punctuation
        text = Regex.Replace(text, @" ,", ",");
        text = Regex.Replace(text, @" \.", ".");
        text = Regex.Replace(text, @" !", "!");
        text = Regex.Replace(text, @" \?", "?");
        text = Regex.Replace(text, @" ;", ";");
        text = Regex.Replace(text, @" :", ":");
        text = Regex.Replace(text, @" '", "'");

        // Remove duplicate quotes
        while (text.Contains("\"\""))
        {
            text = text.Replace("\"\"", "\"");
        }
        while (text.Contains("''"))
        {
            text = text.Replace("''", "'");
        }
        while (text.Contains("``"))
        {
            text = text.Replace("``", "`");
        }

        // Remove extra spaces
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // If text doesn't end with punctuation, quotes, or closing brackets, add a period
        if (!Regex.IsMatch(text, @"[.!?;:,'\u0022\u201C\u201D\u2018\u2019)\]}…。」』】〉》›»]$"))
        {
            text += ".";
        }

        // Validate language
        if (!TtsLanguages.Available.Contains(lang))
        {
            throw new ArgumentException(
                $"Invalid language: {lang}. Available: {string.Join(", ", TtsLanguages.Available)}"
            );
        }

        // Wrap text with language tags
        text = $"<{lang}>" + text + $"</{lang}>";

        return text;
    }

    private static int[] TextToUnicodeValues(string text)
    {
        return text.Select(c => (int)c).ToArray();
    }

    private static float[][][] GetTextMask(long[] textIdsLengths)
    {
        return Helper.LengthToMask(textIdsLengths);
    }

    public (long[][] textIds, float[][][] textMask) Call(
        List<string> textList,
        List<string> langList
    )
    {
        var processedTexts = textList.Select((t, i) => PreprocessText(t, langList[i])).ToList();
        var textIdsLengths = processedTexts.Select(t => (long)t.Length).ToArray();
        var maxLen = textIdsLengths.Max();

        var textIds = new long[textList.Count][];
        for (var i = 0; i < processedTexts.Count; i++)
        {
            textIds[i] = new long[maxLen];
            var unicodeVals = TextToUnicodeValues(processedTexts[i]);
            for (var j = 0; j < unicodeVals.Length; j++)
            {
                if (_indexer.TryGetValue(unicodeVals[j], out var val))
                {
                    textIds[i][j] = val;
                }
            }
        }

        var textMask = GetTextMask(textIdsLengths);
        return (textIds, textMask);
    }
}

// ============================================================================
// TextToSpeech class
// ============================================================================

public class TextToSpeech(
    Config cfgs,
    UnicodeProcessor textProcessor,
    InferenceSession dpOrt,
    InferenceSession textEncOrt,
    InferenceSession vectorEstOrt,
    InferenceSession vocoderOrt
)
{
    private readonly Config _cfgs = cfgs;
    public readonly int SampleRate = cfgs.Ae.SampleRate;
    private readonly int _baseChunkSize = cfgs.Ae.BaseChunkSize;
    private readonly int _chunkCompressFactor = cfgs.TTL.ChunkCompressFactor;
    private readonly int _ldim = cfgs.TTL.LatentDim;

    private (float[][][] noisyLatent, float[][][] latentMask) SampleNoisyLatent(float[] duration)
    {
        var bsz = duration.Length;
        var wavLenMax = duration.Max() * SampleRate;
        var wavLengths = duration.Select(d => (long)(d * SampleRate)).ToArray();
        var chunkSize = _baseChunkSize * _chunkCompressFactor;
        var latentLen = (int)((wavLenMax + chunkSize - 1) / chunkSize);
        var latentDim = _ldim * _chunkCompressFactor;

        // Generate random noise
        var random = new Random();
        var noisyLatent = new float[bsz][][];
        for (var b = 0; b < bsz; b++)
        {
            noisyLatent[b] = new float[latentDim][];
            for (var d = 0; d < latentDim; d++)
            {
                noisyLatent[b][d] = new float[latentLen];
                for (var t = 0; t < latentLen; t++)
                {
                    // Box-Muller transform for normal distribution
                    var u1 = 1.0 - random.NextDouble();
                    var u2 = 1.0 - random.NextDouble();
                    noisyLatent[b][d][t] = (float)(
                        Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2)
                    );
                }
            }
        }

        var latentMask = Helper.GetLatentMask(wavLengths, _baseChunkSize, _chunkCompressFactor);

        // Apply mask
        for (var b = 0; b < bsz; b++)
        {
            for (var d = 0; d < latentDim; d++)
            {
                for (var t = 0; t < latentLen; t++)
                {
                    noisyLatent[b][d][t] *= latentMask[b][0][t];
                }
            }
        }

        return (noisyLatent, latentMask);
    }

    private (float[] wav, float[] duration) _Infer(
        List<string> textList,
        List<string> langList,
        Style style,
        int totalStep,
        float speed = 1.05f
    )
    {
        var bsz = textList.Count;
        if (bsz != style.TtlShape[0])
        {
            throw new ArgumentException("Number of texts must match number of style vectors");
        }

        // Process text
        var (textIds, textMask) = textProcessor.Call(textList, langList);
        var textIdsShape = new long[] { bsz, textIds[0].Length };
        var textMaskShape = new long[] { bsz, 1, textMask[0][0].Length };

        var textIdsTensor = Helper.IntArrayToTensor(textIds, textIdsShape);
        var textMaskTensor = Helper.ArrayToTensor(textMask, textMaskShape);

        var styleTtlTensor = new DenseTensor<float>(
            style.Ttl,
            style.TtlShape.Select(x => (int)x).ToArray()
        );
        var styleDpTensor = new DenseTensor<float>(
            style.Dp,
            style.DpShape.Select(x => (int)x).ToArray()
        );

        // Run duration predictor
        var dpInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("text_ids", textIdsTensor),
            NamedOnnxValue.CreateFromTensor("style_dp", styleDpTensor),
            NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor),
        };
        using var dpOutputs = dpOrt.Run(dpInputs);
        var durOnnx = dpOutputs.First(o => o.Name == "duration").AsTensor<float>().ToArray();

        // Apply speed factor to duration
        for (var i = 0; i < durOnnx.Length; i++)
        {
            durOnnx[i] /= speed;
        }

        // Run text encoder
        var textEncInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("text_ids", textIdsTensor),
            NamedOnnxValue.CreateFromTensor("style_ttl", styleTtlTensor),
            NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor),
        };
        using var textEncOutputs = textEncOrt.Run(textEncInputs);
        var textEmbTensor = textEncOutputs.First(o => o.Name == "text_emb").AsTensor<float>();

        // Sample noisy latent
        var (xt, latentMask) = SampleNoisyLatent(durOnnx);
        var latentShape = new long[] { bsz, xt[0].Length, xt[0][0].Length };
        var latentMaskShape = new long[] { bsz, 1, latentMask[0][0].Length };

        var totalStepArray = Enumerable.Repeat((float)totalStep, bsz).ToArray();

        // Iterative denoising
        for (var step = 0; step < totalStep; step++)
        {
            var currentStepArray = Enumerable.Repeat((float)step, bsz).ToArray();

            var vectorEstInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(
                    "noisy_latent",
                    Helper.ArrayToTensor(xt, latentShape)
                ),
                NamedOnnxValue.CreateFromTensor("text_emb", textEmbTensor),
                NamedOnnxValue.CreateFromTensor("style_ttl", styleTtlTensor),
                NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor),
                NamedOnnxValue.CreateFromTensor(
                    "latent_mask",
                    Helper.ArrayToTensor(latentMask, latentMaskShape)
                ),
                NamedOnnxValue.CreateFromTensor(
                    "total_step",
                    new DenseTensor<float>(totalStepArray, new int[] { bsz })
                ),
                NamedOnnxValue.CreateFromTensor(
                    "current_step",
                    new DenseTensor<float>(currentStepArray, new int[] { bsz })
                ),
            };

            using var vectorEstOutputs = vectorEstOrt.Run(vectorEstInputs);
            var denoisedLatent = vectorEstOutputs
                .First(o => o.Name == "denoised_latent")
                .AsTensor<float>();

            // Update xt
            var idx = 0;
            for (var b = 0; b < bsz; b++)
            {
                for (var d = 0; d < xt[b].Length; d++)
                {
                    for (var t = 0; t < xt[b][d].Length; t++)
                    {
                        xt[b][d][t] = denoisedLatent.GetValue(idx++);
                    }
                }
            }
        }

        // Run vocoder
        var vocoderInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("latent", Helper.ArrayToTensor(xt, latentShape)),
        };
        using var vocoderOutputs = vocoderOrt.Run(vocoderInputs);
        var wavTensor = vocoderOutputs.First(o => o.Name == "wav_tts").AsTensor<float>();

        return (wavTensor.ToArray(), durOnnx);
    }

    public (float[] wav, float[] duration) Call(
        string text,
        string lang,
        Style style,
        int totalStep,
        float speed = 1.05f,
        float silenceDuration = 0.3f
    )
    {
        if (style.TtlShape[0] != 1)
        {
            throw new ArgumentException("Single speaker text to speech only supports single style");
        }

        text = Helper.NormalizeSpeechText(text);
        var maxLen = (lang == "ko" || lang == "ja") ? 120 : 300;
        var textList = Helper.ChunkText(text, maxLen);
        var wavCat = new List<float>();
        var durCat = 0.0f;

        foreach (var chunk in textList)
        {
            var (wav, duration) = _Infer([chunk], [lang], style, totalStep, speed);

            if (wavCat.Count == 0)
            {
                wavCat.AddRange(wav);
                durCat = duration[0];
            }
            else
            {
                var silenceLen = (int)(silenceDuration * SampleRate);
                var silence = new float[silenceLen];
                wavCat.AddRange(silence);
                wavCat.AddRange(wav);
                durCat += duration[0] + silenceDuration;
            }
        }

        return (wavCat.ToArray(), [durCat]);
    }

    public (float[] wav, float[] duration) Batch(
        List<string> textList,
        List<string> langList,
        Style style,
        int totalStep,
        float speed = 1.05f
    )
    {
        return _Infer(textList, langList, style, totalStep, speed);
    }
}

// ============================================================================
// Helper class with utility functions
// ============================================================================

public static class Helper
{
    // ============================================================================
    // Utility functions
    // ============================================================================

    private static readonly Regex SpeechLinkPattern = new(
        @"(?ix)
          \b(
              https?://[^\s<>()\[\]{}]+
              |
              www\.[^\s<>()\[\]{}]+
              |
              (?:[a-z0-9-]+\.)+[a-z]{2,}(?:/[^\s<>()\[\]{}]+)?
          )",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly HashSet<string> CommonSecondLevelTlds = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "ac",
        "co",
        "com",
        "edu",
        "gov",
        "go",
        "mil",
        "ne",
        "net",
        "or",
        "org",
    };

    public static string NormalizeSpeechText(string text)
    {
        var result = text;

        if (!string.IsNullOrWhiteSpace(result))
        {
            result = SpeechLinkPattern.Replace(result, match => GetSiteNameFromLink(match.Value));
        }

        return result;
    }

    private static string GetSiteNameFromLink(string rawLink)
    {
        var result = rawLink.Trim();

        if (TryGetHostFromLink(result, out var host))
        {
            result = GetSiteNameFromHost(host);
        }

        return result;
    }

    private static bool TryGetHostFromLink(string rawLink, out string host)
    {
        var result = string.Empty;

        var link = rawLink.Trim().TrimEnd('.', ',', '!', '?', ';', ':', ')', ']', '}', '>');
        if (Uri.TryCreate(link, UriKind.Absolute, out var absoluteUri))
        {
            result = absoluteUri.Host;
        }
        else if (Uri.TryCreate($"https://{link}", UriKind.Absolute, out absoluteUri))
        {
            result = absoluteUri.Host;
        }

        host = result;
        return !string.IsNullOrWhiteSpace(host);
    }

    private static string GetSiteNameFromHost(string host)
    {
        var result = host.Trim('.').ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(result))
        {
            var labels = result.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (labels.Length == 1)
            {
                result = labels[0];
            }
            else if (
                labels.Length >= 3
                && labels[^1].Length == 2
                && CommonSecondLevelTlds.Contains(labels[^2])
            )
            {
                result = labels[^3];
            }
            else
            {
                result = labels[^2];
            }
        }

        return result;
    }

    public static float[][][] LengthToMask(long[] lengths, long maxLen = -1)
    {
        if (maxLen == -1)
        {
            maxLen = lengths.Max();
        }

        var mask = new float[lengths.Length][][];
        for (var i = 0; i < lengths.Length; i++)
        {
            mask[i] = new float[1][];
            mask[i][0] = new float[maxLen];
            for (var j = 0; j < maxLen; j++)
            {
                mask[i][0][j] = j < lengths[i] ? 1.0f : 0.0f;
            }
        }
        return mask;
    }

    public static float[][][] GetLatentMask(
        long[] wavLengths,
        int baseChunkSize,
        int chunkCompressFactor
    )
    {
        var latentSize = baseChunkSize * chunkCompressFactor;
        var latentLengths = wavLengths.Select(len => (len + latentSize - 1) / latentSize).ToArray();
        return LengthToMask(latentLengths);
    }

    // ============================================================================
    // ONNX model loading
    // ============================================================================

    public static InferenceSession LoadOnnx(string onnxPath, SessionOptions opts)
    {
        return new InferenceSession(onnxPath, opts);
    }

    public static (
        InferenceSession dp,
        InferenceSession textEnc,
        InferenceSession vectorEst,
        InferenceSession vocoder
    ) LoadOnnxAll(string onnxDir, SessionOptions opts)
    {
        var dpPath = Path.Combine(onnxDir, "duration_predictor.onnx");
        var textEncPath = Path.Combine(onnxDir, "text_encoder.onnx");
        var vectorEstPath = Path.Combine(onnxDir, "vector_estimator.onnx");
        var vocoderPath = Path.Combine(onnxDir, "vocoder.onnx");

        return (
            LoadOnnx(dpPath, opts),
            LoadOnnx(textEncPath, opts),
            LoadOnnx(vectorEstPath, opts),
            LoadOnnx(vocoderPath, opts)
        );
    }

    // ============================================================================
    // Configuration loading
    // ============================================================================

    public static Config LoadCfgs(string onnxDir)
    {
        var cfgPath = Path.Combine(onnxDir, "tts.json");
        var json = File.ReadAllText(cfgPath);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new Config
        {
            Ae = new Config.AeConfig
            {
                SampleRate = root.GetProperty("ae").GetProperty("sample_rate").GetInt32(),
                BaseChunkSize = root.GetProperty("ae").GetProperty("base_chunk_size").GetInt32(),
            },
            TTL = new Config.TTLConfig
            {
                ChunkCompressFactor = root.GetProperty("ttl")
                    .GetProperty("chunk_compress_factor")
                    .GetInt32(),
                LatentDim = root.GetProperty("ttl").GetProperty("latent_dim").GetInt32(),
            },
        };
    }

    public static UnicodeProcessor LoadTextProcessor(string onnxDir)
    {
        var unicodeIndexerPath = Path.Combine(onnxDir, "unicode_indexer.json");
        return new UnicodeProcessor(unicodeIndexerPath);
    }

    // ============================================================================
    // Voice style loading
    // ============================================================================

    public static Style LoadVoiceStyle(
        List<string> voiceStylePaths,
        bool verbose = false,
        ILogger? logger = null
    )
    {
        var bsz = voiceStylePaths.Count;

        // Read first file to get dimensions
        var firstJson = File.ReadAllText(voiceStylePaths[0]);
        using var firstDoc = JsonDocument.Parse(firstJson);
        var firstRoot = firstDoc.RootElement;

        var ttlDims = ParseInt64Array(firstRoot.GetProperty("style_ttl").GetProperty("dims"));
        var dpDims = ParseInt64Array(firstRoot.GetProperty("style_dp").GetProperty("dims"));

        var ttlDim1 = ttlDims[1];
        var ttlDim2 = ttlDims[2];
        var dpDim1 = dpDims[1];
        var dpDim2 = dpDims[2];

        // Pre-allocate arrays with full batch size
        var ttlSize = (int)(bsz * ttlDim1 * ttlDim2);
        var dpSize = (int)(bsz * dpDim1 * dpDim2);
        var ttlFlat = new float[ttlSize];
        var dpFlat = new float[dpSize];

        // Fill in the data
        for (var i = 0; i < bsz; i++)
        {
            var json = File.ReadAllText(voiceStylePaths[i]);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Flatten data
            var ttlData3D = ParseFloat3DArray(root.GetProperty("style_ttl").GetProperty("data"));
            var ttlDataFlat = new List<float>();
            foreach (var batch in ttlData3D)
            {
                foreach (var row in batch)
                {
                    ttlDataFlat.AddRange(row);
                }
            }

            var dpData3D = ParseFloat3DArray(root.GetProperty("style_dp").GetProperty("data"));
            var dpDataFlat = new List<float>();
            foreach (var batch in dpData3D)
            {
                foreach (var row in batch)
                {
                    dpDataFlat.AddRange(row);
                }
            }

            // Copy to pre-allocated array
            var ttlOffset = (int)(i * ttlDim1 * ttlDim2);
            ttlDataFlat.CopyTo(ttlFlat, ttlOffset);

            var dpOffset = (int)(i * dpDim1 * dpDim2);
            dpDataFlat.CopyTo(dpFlat, dpOffset);
        }

        var ttlShape = new long[] { bsz, ttlDim1, ttlDim2 };
        var dpShape = new long[] { bsz, dpDim1, dpDim2 };

        if (verbose)
        {
            logger?.LogInformation("Loaded {Bsz} voice styles", bsz);
        }

        return new Style(ttlFlat, ttlShape, dpFlat, dpShape);
    }

    private static float[][][] ParseFloat3DArray(JsonElement element)
    {
        var result = new List<float[][]>();
        foreach (var batch in element.EnumerateArray())
        {
            var batch2D = new List<float[]>();
            foreach (var row in batch.EnumerateArray())
            {
                var rowData = new List<float>();
                foreach (var val in row.EnumerateArray())
                {
                    rowData.Add(val.GetSingle());
                }
                batch2D.Add(rowData.ToArray());
            }
            result.Add(batch2D.ToArray());
        }
        return result.ToArray();
    }

    private static long[] ParseInt64Array(JsonElement element)
    {
        var result = new List<long>();
        foreach (var val in element.EnumerateArray())
        {
            result.Add(val.GetInt64());
        }
        return result.ToArray();
    }

    // ============================================================================
    // TextToSpeech loading
    // ============================================================================

    public static TextToSpeech LoadTextToSpeech(
        string onnxDir,
        bool useGpu = false,
        ILogger? logger = null
    )
    {
        var opts = new SessionOptions();
        if (useGpu)
        {
            throw new NotImplementedException("GPU mode is not supported yet");
        }
        else
        {
            logger?.LogInformation("Using CPU for inference");
        }

        var cfgs = LoadCfgs(onnxDir);
        var (dpOrt, textEncOrt, vectorEstOrt, vocoderOrt) = LoadOnnxAll(onnxDir, opts);
        var textProcessor = LoadTextProcessor(onnxDir);

        return new TextToSpeech(cfgs, textProcessor, dpOrt, textEncOrt, vectorEstOrt, vocoderOrt);
    }

    // ============================================================================
    // WAV file writing
    // ============================================================================

    public static void WriteWavFile(string filename, float[] audioData, int sampleRate)
    {
        using var writer = new BinaryWriter(File.Open(filename, FileMode.Create));

        var numChannels = 1;
        var bitsPerSample = 16;
        var byteRate = sampleRate * numChannels * bitsPerSample / 8;
        var blockAlign = (short)(numChannels * bitsPerSample / 8);
        var dataSize = audioData.Length * bitsPerSample / 8;

        // RIFF header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // fmt chunk size
        writer.Write((short)1); // audio format (PCM)
        writer.Write((short)numChannels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)bitsPerSample);

        // data chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        // Write audio data
        foreach (var sample in audioData)
        {
            var clamped = Math.Max(-1.0f, Math.Min(1.0f, sample));
            var intSample = (short)(clamped * 32767);
            writer.Write(intSample);
        }
    }

    // ============================================================================
    // Tensor conversion utilities
    // ============================================================================

    public static DenseTensor<float> ArrayToTensor(float[][][] array, long[] dims)
    {
        var flat = new List<float>();
        foreach (var batch in array)
        {
            foreach (var row in batch)
            {
                flat.AddRange(row);
            }
        }
        return new DenseTensor<float>(flat.ToArray(), dims.Select(x => (int)x).ToArray());
    }

    public static DenseTensor<long> IntArrayToTensor(long[][] array, long[] dims)
    {
        var flat = new List<long>();
        foreach (var row in array)
        {
            flat.AddRange(row);
        }
        return new DenseTensor<long>(flat.ToArray(), dims.Select(x => (int)x).ToArray());
    }

    // ============================================================================
    // Timer utility
    // ============================================================================

    public static T Timer<T>(string name, Func<T> func, ILogger? logger = null)
    {
        var start = DateTime.Now;
        logger?.LogInformation("{Name}...", name);
        var result = func();
        var elapsed = (DateTime.Now - start).TotalSeconds;
        logger?.LogInformation("  -> {Name} completed in {Elapsed:F2} sec", name, elapsed);
        return result;
    }

    public static string SanitizeFilename(string text, int maxLen)
    {
        var result = new StringBuilder();
        var count = 0;
        foreach (var c in text)
        {
            if (count >= maxLen)
                break;
            if (char.IsLetterOrDigit(c))
            {
                result.Append(c);
            }
            else
            {
                result.Append('_');
            }
            count++;
        }
        return result.ToString();
    }

    // ============================================================================
    // Chunk text
    // ============================================================================

    public static List<string> ChunkText(string text, int maxLen = 300)
    {
        var chunks = new List<string>();

        // Split by paragraph (two or more newlines)
        var paragraphRegex = new Regex(@"\n\s*\n+");
        var paragraphs = paragraphRegex
            .Split(text.Trim())
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        // Split by sentence boundaries, excluding abbreviations
        var sentenceRegex = new Regex(
            @"(?<!Mr\.|Mrs\.|Ms\.|Dr\.|Prof\.|Sr\.|Jr\.|Ph\.D\.|etc\.|e\.g\.|i\.e\.|vs\.|Inc\.|Ltd\.|Co\.|Corp\.|St\.|Ave\.|Blvd\.)(?<!\b[A-Z]\.)(?<=[.!?])\s+"
        );

        foreach (var paragraph in paragraphs)
        {
            var sentences = sentenceRegex.Split(paragraph);
            var currentChunk = "";

            foreach (var sentence in sentences)
            {
                if (string.IsNullOrEmpty(sentence))
                    continue;

                if (currentChunk.Length + sentence.Length + 1 <= maxLen)
                {
                    if (!string.IsNullOrEmpty(currentChunk))
                    {
                        currentChunk += " ";
                    }
                    currentChunk += sentence;
                }
                else
                {
                    if (!string.IsNullOrEmpty(currentChunk))
                    {
                        chunks.Add(currentChunk.Trim());
                    }
                    currentChunk = sentence;
                }
            }

            if (!string.IsNullOrEmpty(currentChunk))
            {
                chunks.Add(currentChunk.Trim());
            }
        }

        // If no chunks were created, return the original text
        if (chunks.Count == 0)
        {
            chunks.Add(text.Trim());
        }

        return chunks;
    }
}
