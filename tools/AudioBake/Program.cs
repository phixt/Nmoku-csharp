// AudioBake —— 音频离线烘焙工具（不进 Nmoku.sln）
//
// 2026-11-27 正式音频升级：
//   - BGM（5 轨：menu/game/game_2/guide/pause）与胜负短乐句（sfx_win/sfx_lose）已由
//     ../programmed-music/out/nmoku_ogg/ 正式交付（REAPER 真音源管线）。
//   - 本工具默认（--sfx-only）仅生成高质感棋子落盘与 UI 微反馈采样（44.1kHz 16-bit PCM WAV）：
//       * sfx_stone_black.wav —— 黑子玄石双敲（主落盘 330→135Hz + 28ms 490→220Hz 回弹）
//       * sfx_stone_white.wav —— 白子玉石双敲（主落盘 620→270Hz + 22ms 940→430Hz 晶脆回弹）
//       * sfx_click.wav       —— 通用中性落盘/点击（柔和双敲，兼容旧 PlaySfx(Click) 路径）
//       * sfx_hover.wav       —— 极轻晶体空气微触（替代旧 440Hz 正弦蜂鸣）
//     参数精确对齐 programmed-music/tools/nmoku_bgm_audition.html 试听台实测配方。
//   - 传 --legacy-all 可重烘旧版全套占位（仅用于回溯对照）。
//
// 用法：dotnet run --project tools/AudioBake -- [--sfx-only|--legacy-all] <输出目录>

using System.Text;

bool legacyAll = args.Contains("--legacy-all", StringComparer.OrdinalIgnoreCase);
string outDir = args.LastOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? ".";
Directory.CreateDirectory(outDir);

if (legacyAll)
{
    BakeLegacySfx(outDir);
    BakeLegacyBgm(outDir);
}
else
{
    BakeFormalTactileSfx(outDir);
}
return 0;

// ---------------- 正式落子 & UI 触感 SFX（对齐 nmoku_bgm_audition.html 配方，44.1kHz） ----------------

void BakeFormalTactileSfx(string dir)
{
    const int sr = 44100;

    // 1. 黑子（低沉厚实玄石感）：主落盘 330->135Hz + 1350Hz 带通瞬态；28ms 后高跳 1.48x 回弹轻敲
    WriteToneDoubleTap(
        Path.Combine(dir, "sfx_stone_black.wav"),
        sr,
        totalSec: 0.115,
        seed: 20261127,
        primary: new StoneTap(0.0, 0.72, 330, 135, 1350, 2.0, 0.032, 0.30, 0.30),
        rebound: new StoneTap(0.028, 0.35, 490, 220, 2050, 2.6, 0.022, 0.24, 0.26),
        targetPeak: 0.30);

    // 2. 白子（清亮脆润玉石感）：主落盘 620->270Hz + 2650Hz 带通瞬态；22ms 后高跳 1.52x 晶脆回弹
    WriteToneDoubleTap(
        Path.Combine(dir, "sfx_stone_white.wav"),
        sr,
        totalSec: 0.105,
        seed: 20261128,
        primary: new StoneTap(0.0, 0.66, 620, 270, 2650, 2.8, 0.026, 0.26, 0.28),
        rebound: new StoneTap(0.022, 0.32, 940, 430, 3900, 3.2, 0.018, 0.22, 0.24),
        targetPeak: 0.27);

    // 3. 通用点击（柔和中性温润石触，替代刺耳方波 660/1320Hz）
    WriteToneDoubleTap(
        Path.Combine(dir, "sfx_click.wav"),
        sr,
        totalSec: 0.100,
        seed: 20261129,
        primary: new StoneTap(0.0, 0.65, 460, 195, 1900, 2.4, 0.028, 0.26, 0.26),
        rebound: new StoneTap(0.024, 0.28, 690, 310, 2900, 2.8, 0.019, 0.20, 0.22),
        targetPeak: 0.26);

    // 4. 轻触悬停（极轻柔和晶体微息，替代 440Hz 纯正弦）
    WriteHoverMicroTick(Path.Combine(dir, "sfx_hover.wav"), sr, seed: 20261130, targetPeak: 0.10);
}

void WriteToneDoubleTap(string path, int sr, double totalSec, int seed, StoneTap primary, StoneTap rebound, double targetPeak)
{
    int n = (int)(sr * totalSec);
    var buf = new double[n];
    var rng = new Random(seed);
    AddStoneTap(buf, sr, primary, rng);
    AddStoneTap(buf, sr, rebound, rng);
    // 末端 8ms 柔化防点击尾截断
    int tailFade = Math.Min((int)(sr * 0.008), n);
    for (int i = 0; i < tailFade; i++)
    {
        buf[n - 1 - i] *= (double)i / tailFade;
    }
    Normalize(buf, targetPeak);
    WriteWav(path, buf, sr);
    Console.WriteLine($"sfx {Path.GetFileNameWithoutExtension(path)}: {n} samples @ {sr}Hz (peak={targetPeak:0.00})");
}

void AddStoneTap(double[] buf, int sr, StoneTap tap, Random rng)
{
    int startIdx = (int)(tap.DelaySec * sr);

    // A. 琴体/石体共振（三角波 + 指数下滑频 + 2ms 起音 + 指数衰减）
    int bodyLen = (int)((tap.BodyDur + 0.006) * sr);
    double phase = 0;
    double freqRampDur = Math.Max(0.001, tap.BodyDur * 0.78);
    double attackSec = 0.002;
    for (int i = 0; i < bodyLen && startIdx + i < buf.Length; i++)
    {
        double t = (double)i / sr;
        double f = t < freqRampDur
            ? tap.BodyStart * Math.Pow(tap.BodyEnd / tap.BodyStart, t / freqRampDur)
            : tap.BodyEnd;
        phase += f / sr;
        double wave = SampleWave(Waveform.Triangle, phase);
        double env;
        if (t < attackSec)
        {
            env = t / attackSec;
        }
        else if (t < tap.BodyDur)
        {
            // 对齐 Web Audio exponentialRampToValueAtTime(0.0001, bodyDur)
            double u = (t - attackSec) / Math.Max(0.001, tap.BodyDur - attackSec);
            env = Math.Pow(0.0005, u);
        }
        else
        {
            env = 0;
        }
        buf[startIdx + i] += wave * tap.Level * tap.BodyMix * env;
    }

    // B. 碰撞瞬态噪声（15ms 指数包络白噪经二阶 RBJ 带通滤波）
    int noiseLen = (int)(sr * 0.022);
    var noiseBuf = new double[noiseLen];
    int rawLen = (int)(sr * 0.015);
    for (int i = 0; i < rawLen; i++)
    {
        double env = Math.Exp(-i / (rawLen * 0.25));
        noiseBuf[i] = (rng.NextDouble() * 2.0 - 1.0) * env;
    }
    ApplyBandpass(noiseBuf, sr, tap.NoiseFreq, tap.NoiseQ);
    for (int i = 0; i < noiseLen && startIdx + i < buf.Length; i++)
    {
        buf[startIdx + i] += noiseBuf[i] * tap.Level * tap.NoiseMix;
    }
}

void WriteHoverMicroTick(string path, int sr, int seed, double targetPeak)
{
    int n = (int)(sr * 0.032);
    var buf = new double[n];
    var rng = new Random(seed);
    double phase = 0;
    for (int i = 0; i < n; i++)
    {
        double t = (double)i / sr;
        double f = 1480.0 * Math.Pow(980.0 / 1480.0, t / 0.032);
        phase += f / sr;
        double env = (t < 0.002 ? t / 0.002 : 1.0) * Math.Exp(-t / 0.008);
        buf[i] = (Math.Sin(2 * Math.PI * phase) * 0.7 + (rng.NextDouble() * 2 - 1) * 0.12) * env;
    }
    ApplyBandpass(buf, sr, 1800, 1.4);
    Normalize(buf, targetPeak);
    WriteWav(path, buf, sr);
    Console.WriteLine($"sfx sfx_hover: {n} samples @ {sr}Hz (peak={targetPeak:0.00})");
}

// RBJ Audio EQ Cookbook 带通滤波器（constant skirt gain, peak gain = Q → 归一化）
static void ApplyBandpass(double[] x, int sr, double freq, double q)
{
    double w0 = 2.0 * Math.PI * Math.Clamp(freq, 40, sr * 0.45) / sr;
    double alpha = Math.Sin(w0) / (2.0 * Math.Max(0.2, q));
    double b0 = alpha;
    double b1 = 0.0;
    double b2 = -alpha;
    double a0 = 1.0 + alpha;
    double a1 = -2.0 * Math.Cos(w0);
    double a2 = 1.0 - alpha;

    b0 /= a0;
    b1 /= a0;
    b2 /= a0;
    a1 /= a0;
    a2 /= a0;

    double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
    for (int i = 0; i < x.Length; i++)
    {
        double x0 = x[i];
        double y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
        x2 = x1;
        x1 = x0;
        y2 = y1;
        y1 = y0;
        x[i] = y0;
    }
}

// ---------------- 旧版占位 SFX/BGM（保留 --legacy-all 仅作对照） ----------------

void BakeLegacySfx(string dir)
{
    Bake("sfx_click", [660, 1320], 0.07, 0.4, Waveform.Square, null);
    Bake("sfx_win", [523, 659, 784], 0.4, 0.5, Waveform.Triangle, (523, 1047));
    Bake("sfx_lose", [220, 330], 0.5, 0.45, Waveform.Triangle, (440, 220));
    Bake("sfx_hover", [440, 443], 0.04, 0.2, Waveform.Sine, null);

    void Bake(string name, double[] freqs, double dur, double vol, Waveform wave, (double From, double To)? glide)
    {
        int sr = 22050;
        int n = (int)(sr * dur);
        var buf = new double[n];
        int attack = Math.Min((int)(sr * 0.005), n);
        int release = Math.Min((int)(sr * 0.06), n);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double s = 0;
            if (glide is { } g)
            {
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
        Console.WriteLine($"legacy sfx {name}: {n} samples @ {sr}Hz");
    }
}

void BakeLegacyBgm(string dir)
{
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

                AddNote(buf, sr, bass.Wave, notes[0] * Pow2(bass.Oct), start, chordDur, attack, release,
                    bass.Gain * baseGain, style, isBass: true);
                foreach (double f in notes)
                {
                    AddNote(buf, sr, chord.Wave, f * Pow2(chord.Oct), start, chordDur, attack, release,
                        chord.Gain * baseGain, style);
                }
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
        Console.WriteLine($"legacy bgm {name}: {buf.Length / (double)sr:0.0}s ({rounds} rounds)");
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
    double len = style == NoteStyle.Staccato ? dur * 0.35 : dur;
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
        buf[begin + i] += SampleWave(wave, freq * t) * gain * env * (isBass ? 1 : 0.9);
    }
}

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
    fs.Write(Encoding.ASCII.GetBytes("RIFF"));
    fs.Write(BitConverter.GetBytes(36 + dataBytes));
    fs.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
    fs.Write(BitConverter.GetBytes(16));
    fs.Write(BitConverter.GetBytes((short)1));
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
record StoneTap(double DelaySec, double Level, double BodyStart, double BodyEnd, double NoiseFreq, double NoiseQ, double BodyDur, double BodyMix, double NoiseMix);
