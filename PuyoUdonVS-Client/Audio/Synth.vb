''' <summary>
''' 効果音とBGMの波形をプログラムで合成する（モノラル 44.1kHz、float）。
''' </summary>
Friend Module Synth
    Private Const SR As Integer = SoundEngine.SampleRate
    Private ReadOnly Rng As New Random(1234)

    Private Function Freq(midi As Double) As Double
        Return 440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0)
    End Function

    Private Function Samples(seconds As Double) As Integer
        Return CInt(seconds * SR)
    End Function

    ''' <summary>buf の start から音を足し込む。wave: 0=矩形, 1=三角, 2=サイン</summary>
    Private Sub AddTone(buf As Single(), start As Integer, seconds As Double, f0 As Double, f1 As Double,
                        vol As Double, wave As Integer, Optional attack As Double = 0.004,
                        Optional release As Double = 0.04, Optional duty As Double = 0.5,
                        Optional decayTo As Double = 1.0, Optional vibrato As Double = 0)
        Dim n = Samples(seconds)
        Dim phase = 0.0
        Dim a = Math.Max(1, Samples(attack))
        Dim r = Math.Max(1, Samples(release))
        For i = 0 To n - 1
            Dim idx = start + i
            If idx < 0 Then Continue For
            If idx >= buf.Length Then Exit For
            Dim t = i / CDbl(n)
            Dim f = f0 + (f1 - f0) * t
            If vibrato > 0 AndAlso i > SR \ 8 Then f *= 1 + vibrato * Math.Sin(2 * Math.PI * 5.5 * i / SR)
            phase += f / SR
            phase -= Math.Floor(phase)
            Dim v As Double
            Select Case wave
                Case 0 : v = If(phase < duty, 1.0, -1.0)
                Case 1 : v = 4 * Math.Abs(phase - 0.5) - 1
                Case Else : v = Math.Sin(2 * Math.PI * phase)
            End Select
            Dim env = 1.0
            If i < a Then env = i / CDbl(a)
            env *= 1.0 + (decayTo - 1.0) * t
            If i > n - r Then env *= (n - i) / CDbl(r)
            buf(idx) += CSng(v * env * vol)
        Next
    End Sub

    Private Sub AddNoise(buf As Single(), start As Integer, seconds As Double, vol As Double,
                         Optional smooth As Double = 0, Optional highpass As Boolean = False)
        Dim n = Samples(seconds)
        Dim lp = 0.0, prev = 0.0
        For i = 0 To n - 1
            Dim idx = start + i
            If idx < 0 Then Continue For
            If idx >= buf.Length Then Exit For
            Dim w = Rng.NextDouble() * 2 - 1
            lp = lp * smooth + w * (1 - smooth)
            Dim v = lp
            If highpass Then
                v = lp - prev
                prev = lp
            End If
            Dim env = Math.Pow(1 - i / CDbl(n), 2)
            buf(idx) += CSng(v * env * vol)
        Next
    End Sub

    Private Function Finish(buf As Single()) As Single()
        For i = 0 To buf.Length - 1
            buf(i) = CSng(Math.Tanh(buf(i)))
        Next
        Return buf
    End Function

    Public Function RenderSfx(s As Sfx) As Single()
        Select Case s
            Case Sfx.Move
                Dim b(Samples(0.03)) As Single
                AddTone(b, 0, 0.03, 700, 700, 0.12, 0, duty:=0.25, release:=0.01)
                Return Finish(b)
            Case Sfx.Rotate
                Dim b(Samples(0.06)) As Single
                AddTone(b, 0, 0.06, 900, 1300, 0.12, 0, duty:=0.25, release:=0.02)
                Return Finish(b)
            Case Sfx.Land
                Dim b(Samples(0.12)) As Single
                AddTone(b, 0, 0.12, 180, 70, 0.5, 2, release:=0.05, decayTo:=0.2)
                AddNoise(b, 0, 0.05, 0.15, smooth:=0.8)
                Return Finish(b)
            Case Sfx.HardDrop
                Dim b(Samples(0.18)) As Single
                AddTone(b, 0, 0.18, 220, 50, 0.7, 2, release:=0.06, decayTo:=0.2)
                AddNoise(b, 0, 0.1, 0.3, smooth:=0.85)
                Return Finish(b)
            Case Sfx.Erase1 To Sfx.Erase7
                ' 連鎖が進むほど高くなる「ぽよん」系の上昇アルペジオ
                Dim chain = s - Sfx.Erase1
                Dim baseNote = 72 + chain * 2
                Dim notes = {0, 4, 7, 12, 16}
                Dim len = 0.055
                Dim b(Samples(len * notes.Length + 0.25)) As Single
                For k = 0 To notes.Length - 1
                    Dim st = Samples(len * k)
                    AddTone(b, st, 0.22, Freq(baseNote + notes(k)), Freq(baseNote + notes(k)) * 1.01, 0.14, 0, duty:=0.25, release:=0.15, decayTo:=0.3)
                    AddTone(b, st, 0.22, Freq(baseNote + notes(k) - 12), Freq(baseNote + notes(k) - 12), 0.12, 2, release:=0.15, decayTo:=0.3)
                Next
                ' すする音っぽいノイズ
                AddNoise(b, 0, 0.25, 0.08, smooth:=0.3, highpass:=True)
                Return Finish(b)
            Case Sfx.GarbageFall
                Dim b(Samples(0.35)) As Single
                AddNoise(b, 0, 0.35, 0.5, smooth:=0.92)
                AddTone(b, 0, 0.3, 110, 45, 0.45, 2, release:=0.1, decayTo:=0.3)
                Return Finish(b)
            Case Sfx.Attack
                Dim b(Samples(0.3)) As Single
                AddNoise(b, 0, 0.3, 0.25, smooth:=0.5, highpass:=True)
                AddTone(b, 0, 0.3, 400, 1600, 0.1, 0, duty:=0.5, release:=0.1)
                Return Finish(b)
            Case Sfx.Beep
                Dim b(Samples(0.15)) As Single
                AddTone(b, 0, 0.15, 660, 660, 0.18, 0, duty:=0.5, release:=0.04)
                Return Finish(b)
            Case Sfx.Go
                Dim b(Samples(0.45)) As Single
                AddTone(b, 0, 0.45, 1320, 1320, 0.18, 0, duty:=0.5, release:=0.15)
                AddTone(b, 0, 0.45, 660, 660, 0.12, 1, release:=0.15)
                Return Finish(b)
            Case Sfx.Win
                Dim notes = {72, 76, 79, 84, 79, 84, 88}
                Dim durs = {0.12, 0.12, 0.12, 0.24, 0.12, 0.12, 0.6}
                Dim b(Samples(durs.Sum() + 0.3)) As Single
                Dim t = 0.0
                For k = 0 To notes.Length - 1
                    AddTone(b, Samples(t), durs(k) + 0.05, Freq(notes(k)), Freq(notes(k)), 0.16, 0, duty:=0.25, release:=0.05)
                    AddTone(b, Samples(t), durs(k) + 0.05, Freq(notes(k) - 24), Freq(notes(k) - 24), 0.2, 1, release:=0.05)
                    t += durs(k)
                Next
                Return Finish(b)
            Case Sfx.Lose
                Dim notes = {71, 67, 64, 59}
                Dim b(Samples(1.4)) As Single
                For k = 0 To notes.Length - 1
                    AddTone(b, Samples(0.25 * k), If(k = 3, 0.6, 0.28), Freq(notes(k)), Freq(notes(k) - If(k = 3, 1, 0)), 0.16, 0, duty:=0.5, release:=0.1, vibrato:=If(k = 3, 0.02, 0))
                Next
                Return Finish(b)
            Case Sfx.Click
                Dim b(Samples(0.05)) As Single
                AddTone(b, 0, 0.05, 1000, 800, 0.12, 0, duty:=0.25, release:=0.02)
                Return Finish(b)
            Case Sfx.Join
                Dim b(Samples(0.3)) As Single
                AddTone(b, 0, 0.12, Freq(76), Freq(76), 0.14, 0, duty:=0.25)
                AddTone(b, Samples(0.12), 0.18, Freq(83), Freq(83), 0.14, 0, duty:=0.25, release:=0.08)
                Return Finish(b)
        End Select
        Return New Single(0) {}
    End Function

    ' ===================== BGM =====================
    ' 祭りばやし風の明るいループ（ニ長調ペンタトニック、150BPM、16小節）。
    ' 1トークン = 8分音符。"-" は前の音を伸ばす、"." は休符。

    Private ReadOnly Melody As String() = {
        "A4 B4 D5 - E5 D5 B4 -",
        "A4 - B4 D5 E5 - - .",
        "F#5 E5 D5 - E5 D5 B4 A4",
        "B4 - - - . . . .",
        "A4 B4 D5 - E5 D5 E5 F#5",
        "A5 - F#5 E5 D5 - . .",
        "E5 F#5 E5 D5 B4 D5 B4 A4",
        "D5 - - - . . . .",
        "F#5 - A5 - B5 A5 F#5 -",
        "E5 - F#5 E5 D5 - . .",
        "F#5 - A5 - B5 - D6 -",
        "B5 A5 F#5 E5 F#5 - - .",
        "A5 - B5 A5 F#5 E5 D5 -",
        "E5 F#5 E5 D5 B4 - . .",
        "A4 B4 D5 E5 F#5 E5 D5 B4",
        "D5 - - - - - . ."}

    ''' <summary>各小節のベースの根音。</summary>
    Private ReadOnly BassRoots As String() = {"D2", "G2", "D2", "B1", "D2", "A1", "A1", "D2", "D2", "A1", "D2", "B1", "D2", "G2", "A1", "D2"}

    Private Function NoteToMidi(name As String) As Integer
        Dim letters = "C D EF G A B"
        Dim n = letters.IndexOf(name(0))
        Dim i = 1
        If name(i) = "#"c Then n += 1 : i += 1
        Dim octave = Integer.Parse(name.Substring(i))
        Return 12 * (octave + 1) + n
    End Function

    Public Function RenderBgm() As Single()
        Const bpm As Double = 150
        Dim eighth = 60.0 / bpm / 2
        Dim totalEighths = Melody.Length * 8
        Dim buf(Samples(eighth * totalEighths) - 1) As Single

        ' メロディ（矩形波）と1オクターブ下の柔らかい重ね（三角波）
        Dim tokens = Melody.SelectMany(Function(bar) bar.Split(" "c, StringSplitOptions.RemoveEmptyEntries)).ToArray()
        Dim i = 0
        While i < tokens.Length
            Dim tk = tokens(i)
            If tk = "-" OrElse tk = "." Then i += 1 : Continue While
            Dim len = 1
            While i + len < tokens.Length AndAlso tokens(i + len) = "-"
                len += 1
            End While
            Dim m = NoteToMidi(tk)
            Dim st = Samples(eighth * i)
            Dim dur = eighth * len * 0.92
            AddTone(buf, st, dur, Freq(m), Freq(m), 0.11, 0, duty:=0.25, release:=0.03, decayTo:=0.75, vibrato:=0.004)
            AddTone(buf, st, dur, Freq(m - 12), Freq(m - 12), 0.06, 1, release:=0.03, decayTo:=0.7)
            i += len
        End While

        ' ベース（三角波）: 根音とオクターブを交互に
        For bar = 0 To BassRoots.Length - 1
            Dim root = NoteToMidi(BassRoots(bar))
            For k = 0 To 7
                If k Mod 2 = 1 Then Continue For
                Dim m = If(k Mod 4 = 0, root, root + 12)
                AddTone(buf, Samples(eighth * (bar * 8 + k)), eighth * 0.9, Freq(m), Freq(m), 0.2, 1, release:=0.03)
            Next
        Next

        ' 太鼓・鉦（ノイズとサイン波）
        For e = 0 To totalEighths - 1
            Dim st = Samples(eighth * e)
            Dim inBar = e Mod 8
            If inBar = 0 OrElse inBar = 4 OrElse (inBar = 7 AndAlso (e \ 8) Mod 2 = 1) Then
                AddTone(buf, st, 0.14, 140, 50, 0.45, 2, attack:=0.002, release:=0.05, decayTo:=0.1) ' どん
            End If
            If inBar = 2 OrElse inBar = 6 Then
                AddNoise(buf, st, 0.1, 0.16, smooth:=0.4) ' かっ
                AddTone(buf, st, 0.05, 330, 300, 0.08, 1, attack:=0.001, release:=0.02)
            End If
            AddNoise(buf, st, 0.03, 0.06, smooth:=0, highpass:=True) ' ちきちき
        Next

        ' つなぎ目でプチノイズが出ないように端を少しだけフェード
        Dim fade = Samples(0.005)
        For k = 0 To fade - 1
            Dim g = CSng(k / fade)
            buf(k) *= g
            buf(buf.Length - 1 - k) *= g
        Next
        Return Finish(buf)
    End Function
End Module
