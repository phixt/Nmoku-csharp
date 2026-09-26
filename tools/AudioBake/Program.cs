// AudioBake —— M5 占位音频生成器（一次性工具，不进 Nmoku.sln）
//
// 产物（migration-plan §10 B20 ⑤）：
//   1. SFX 四件：按源项目 generateWavURI/preloadSFX 参数精确复刻（22050Hz WAV）。
//   2. BGM 四轨占位循环：按 audio_var.md §二 的 BGM_PATTERNS 和声/分层/包络烘少量轮次，
//      不含混响/延迟/LFO 效果器（正式 BGM 走 Strudel 谱面路线，见 assets-src/music/strudel/）。
//
// 用法：dotnet run --project tools/AudioBake -- <输出目录>
// 输出 WAV 为中间产物；BGM 由 scripts/bake-audio.ps1 转 OGG 后入库，SFX WAV 直接入库。

using System.Text;

int sampleRate = args.Length > 0 && args[0] == "--bgm-sr" ? 44100 : 22050;
string outDir = args.Length > 0 ? args[^1] : ".";
Directory.CreateDirectory(outDir);

BakeSfx(outDir);
BakeBgm(outDir);
return 0;

// ---------------- SFX：源 preloadSFX 参数（AudioManager.ts:398-403） ----------------
void BakeSfx(string dir)
{
    // (名, 频率组, 时长, 音量, 波形, 滑音)
    Bake("sfx_click", [660, 1320], 0.07, 0.4, Waveform.Square, null);
    Bake("sfx_win", [523, 659, 784], 0.4, 0.5, Waveform.Triangle, (523, 1047));
    Bake("sfx_lose", [220, 330], 0.5, 0.45, Waveform.Triangle, (440, 220));
    Bake("sfx_hover", [440, 443], 0.04, 0.2, Waveform.Sine, null);

    void Bake(string name, double[] freqs, double dur, double vol, Waveform wave, (double From, double To)? glide)
    {
        int sr = 22050; // 源 generateWavURI 固定采样率
        int n = (int)(sr * dur);
        var buf = new double[n];
        int attack = Math.Min((int)(sr * 0.005), n); // 源公式：5ms 起、60ms 收
        int release = Math.Min((int)(sr * 0.06), n);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double s = 0;
            if (glide is { } g)
            {
                // 源公式：滑音只作用于首个频率，其余频率直叠，最后按频率数取平均
                double f = g.From + (g.To - g.From) * (t / dur);
                for (int k = 0; k < freqs.Length; k++)
                {
                    s += k == 0 ? Math.Sin(2 * Math.PI * f * t) : Math.Sin(2 * Math.PI * freqs[k] * t);
                }
                s /= freqs.Length;
            }
            else
            {
                foreach (double f in freqs)
                {
                    s += SampleWave(wave, f * t);
                }
                s /= freqs.Length;
            }
            double env = 1;
            if (i < attack) env = (double)i / attack;
            if (i > n - release) env = (double)(n - i) / release;
            buf[i] = s * vol * env * 0.7;
        }
        WriteWav(Path.Combine(dir, name + ".wav"), buf, sr);
        Console.WriteLine($"sfx {name}: {n} samples @ {sr}Hz");
    }
}

// ---------------- BGM 占位：BGM_PATTERNS（audio_var.md §二 / AudioManager.ts:93-165） ----------------
void BakeBgm(string dir)
{
    // (名, 和弦进行, 和弦秒数, 层, 触键, 主增益, 轮数)
    Bake("bgm_menu", ["Cmaj7", "Gadd9", "Am7", "Fmaj7"], 2.8,
        new Layer("bass", Waveform.Triangle, 0.16, -1),
        new Layer("chord", Waveform.Saw, 0.065, 0),
        new Layer("arp", Waveform.Sine, 0.045, 1), NoteStyle.Pad, 0.7, 2);
    Bake("bgm_game", ["Am7", "Fmaj7", "Cadd9", "G7"], 0.9,
        new Layer("bass", Waveform.Triangle, 0.16, -1),
        new Layer("chord", Waveform.Saw, 0.08, 0),
        new Layer("arp", Waveform.Square, 0.03, 1), NoteStyle.Staccato, 0.55, 4);
    Bake("bgm_guide", ["Cadd9", "Fadd9", "Cadd9", "G7"], 1.8,
        new Layer("bass", Waveform.Triangle, 0.12, -1),
        new Layer("chord", Waveform.Triangle, 0.06, 0),
        new Layer("pad", Waveform.Triangle, 0.05, 1), NoteStyle.Sustain, 0.5, 2);
    Bake("bgm_pause", ["G", "Gsus", "G", "Gsus"], 3.5,
        new Layer("bass", Waveform.Triangle, 0.08, -1),
        new Layer("chord", Waveform.Triangle, 0.04, 0),
        null, NoteStyle.Sustain, 0.35, 2);

    void Bake(string name, string[] prog, double chordDur, Layer bass, Layer chord, Layer? upper,
        NoteStyle style, double baseGain, int rounds)
    {
        int sr = 44100;
        double roundSec = prog.Length * chordDur;
        var buf = new double[(int)(sr * roundSec * rounds)];
        for (int r = 0; r < rounds; r++)
        {
            for (int ci = 0; ci < prog.Length; ci++)
            {
                double start = (r * prog.Length + ci) * chordDur;
                double[] notes = ChordNotes(prog[ci]);
                double attack = Math.Min(chordDur * 0.08, 0.15);
                double release = Math.Min(chordDur * 0.3, 0.5);

                // 贝斯层：和弦根音降八度
                AddNote(buf, sr, bass.Wave, notes[0] * Pow2(bass.Oct), start, chordDur, attack, release,
                    bass.Gain * baseGain, style, isBass: true);
                // 和弦层：全和弦音
                foreach (double f in notes)
                {
                    AddNote(buf, sr, chord.Wave, f * Pow2(chord.Oct), start, chordDur, attack, release,
                        chord.Gain * baseGain, style);
                }
                // 上层：menu/game=琶音（16 分位均分）、guide=五度+九度长音垫
                if (upper is { } u)
                {
                    if (u.Kind == "arp")
                    {
                        double[] arp = ArpNotes(prog[ci], notes);
                        double step = chordDur / arp.Length;
                        for (int i = 0; i < arp.Length; i++)
                        {
                            AddNote(buf, sr, u.Wave, arp[i] * Pow2(u.Oct), start + i * step, step * 0.7,
                                attack * 0.3, release * 0.2, u.Gain * baseGain, NoteStyle.Staccato);
                        }
                    }
                    else
                    {
                        AddNote(buf, sr, u.Wave, notes[0] * Pow2(7.0 / 12 + u.Oct), start, chordDur,
                            attack, release * 1.3, u.Gain * baseGain, NoteStyle.Sustain);
                        AddNote(buf, sr, u.Wave, notes[0] * Pow2(14.0 / 12 + u.Oct), start, chordDur,
                            attack, release * 1.3, u.Gain * baseGain, NoteStyle.Sustain);
                    }
                }
            }
        }
        Normalize(buf, 0.85);
        WriteWav(Path.Combine(dir, name + ".wav"), buf, sr);
        Console.WriteLine($"bgm {name}: {buf.Length / (double)sr:0.0}s ({rounds} rounds)");
    }
}

// ---------------- 合成原语 ----------------

static double Pow2(double oct) => Math.Pow(2, oct);

static double SampleWave(Waveform w, double cycles)
{
    double ph = cycles - Math.Floor(cycles);
    return w switch
    {
        Waveform.Sine => Math.Sin(2 * Math.PI * ph),
        Waveform.Triangle => 2 / Math.PI * Math.Asin(Math.Sin(2 * Math.PI * ph)),
        Waveform.Square => Math.Sin(2 * Math.PI * ph) >= 0 ? 0.5 : -0.5,
        Waveform.Saw => Saw(ph),
        _ => 0,
    };

    // 方波/锯齿直接求值会产生混叠（高频折叠回可听域），锯齿用前 8 个谐波叠加近似；
    // 占位轨够用，正式 BGM 由 Strudel 渲染替代
    static double Saw(double ph)
    {
        double s = 0;
        for (int h = 1; h <= 8; h++)
        {
            s += Math.Sin(2 * Math.PI * h * ph) / h;
        }
        return s * 2 / Math.PI;
    }
}

static void AddNote(double[] buf, int sr, Waveform wave, double freq, double start, double dur,
    double attack, double release, double gain, NoteStyle style, bool isBass = false)
{
    double len = style == NoteStyle.Staccato ? dur * 0.35 : dur; // 源：staccato 音长 35%
    int begin = (int)(start * sr);
    int n = Math.Min((int)(len * sr) + (int)(release * sr), buf.Length - begin);
    for (int i = 0; i < n; i++)
    {
        double t = (double)i / sr;
        double env;
        if (t < attack) env = t / attack;
        else if (t < len) env = 1;
        else env = Math.Max(0, 1 - (t - len) / release);
        if (env <= 0) continue;
        // 贝斯降一个八度的音高频低，直接按频率求值
        buf[begin + i] += SampleWave(wave, freq * t) * gain * env * (isBass ? 1 : 0.9);
    }
}

// ---------------- 和弦与音名表（AudioManager.ts:22-58 子集） ----------------

static double[] ChordNotes(string name) => name switch
{
    "G" => N("G4 B4 D5"),
    "Gsus" => N("G4 C5 D5"),
    "Am7" => N("A3 C4 E4 G4"),
    "Fmaj7" => N("F4 A4 C5 E5"),
    "Cmaj7" => N("C4 E4 G4 B4"),
    "Gadd9" => N("G4 B4 D5 A5"),
    "Cadd9" => N("C4 E4 G4 D5"),
    "G7" => N("G4 B4 D5 F5"),
    "Fadd9" => N("F4 A4 C5 G5"),
    _ => throw new ArgumentException(name),
};

// menu 的 add9 琶音反转组（AudioManager.ts:42-50）；game 用和弦音顺序
static double[] ArpNotes(string name, double[] chord) => name switch
{
    "Cmaj7" => N("C4 E4 G4 B4 C5 B4 G4 E4"),
    "Gadd9" => N("G3 B3 D4 A4 B4 A4 D4 B3"),
    "Am7" => N("A3 C4 E4 G4 A4 G4 E4 C4"),
    "Fmaj7" => N("F3 A3 C4 E4 F4 E4 C4 A3"),
    _ => chord,
};

static double[] N(string names) => names.Split(' ').Select(NoteFreq).ToArray();

static double NoteFreq(string name)
{
    // A4=440 十二平均律；支持 F#2 / Bb3 形式
    int i = 0;
    double semis = name[0] switch
    {
        'C' => -9, 'D' => -7, 'E' => -5, 'F' => -4, 'G' => -2, 'A' => 0, 'B' => 2,
        _ => throw new ArgumentException(name),
    };
    i++;
    if (i < name.Length && name[i] == '#') { semis++; i++; }
    if (i < name.Length && name[i] == 'b') { semis--; i++; }
    int octave = name[i..] is { Length: > 0 } o ? int.Parse(o) : throw new ArgumentException(name);
    return 440.0 * Math.Pow(2, semis / 12.0 + octave - 4);
}

// ---------------- WAV 写出 ----------------

static void Normalize(double[] buf, double peak)
{
    double max = buf.Max(Math.Abs);
    if (max > 1e-9)
    {
        double k = peak / max;
        for (int i = 0; i < buf.Length; i++) buf[i] *= k;
    }
}

static void WriteWav(string path, double[] buf, int sr)
{
    using var fs = new FileStream(path, FileMode.Create);
    short bits = 16, channels = 1;
    int dataBytes = buf.Length * 2;
    var head = Encoding.ASCII.GetBytes("RIFF");
    fs.Write(head);
    fs.Write(BitConverter.GetBytes(36 + dataBytes));
    fs.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
    fs.Write(BitConverter.GetBytes(16));
    fs.Write(BitConverter.GetBytes((short)1)); // PCM
    fs.Write(BitConverter.GetBytes(channels));
    fs.Write(BitConverter.GetBytes(sr));
    fs.Write(BitConverter.GetBytes(sr * channels * bits / 8));
    fs.Write(BitConverter.GetBytes((short)(channels * bits / 8)));
    fs.Write(BitConverter.GetBytes(bits));
    fs.Write(Encoding.ASCII.GetBytes("data"));
    fs.Write(BitConverter.GetBytes(dataBytes));
    foreach (double s in buf)
    {
        short v = (short)Math.Clamp((int)(s * 32767), short.MinValue, short.MaxValue);
        fs.Write(BitConverter.GetBytes(v));
    }
}

enum Waveform { Sine, Triangle, Square, Saw }
enum NoteStyle { Pad, Staccato, Sustain }

record Layer(string Kind, Waveform Wave, double Gain, int Oct);
