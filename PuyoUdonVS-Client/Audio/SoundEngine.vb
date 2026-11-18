Imports System.IO
Imports NAudio.Wave
Imports NAudio.Wave.SampleProviders

''' <summary>効果音。Resources\SE\ の同名 mp3 に対応する。</summary>
Public Enum Sfx
    Move      ' move.mp3     : 左右移動
    Click     ' click.mp3    : ボタン
    Fallen    ' fallen.mp3   : うどんの落下（着地）
    Rotate    ' rotate.mp3   : 回転
    Rensa     ' rensa.mp3    : 連鎖（消える）
    Ozyama    ' ozyama.mp3   : おじゃまが降ってきた
    Matching  ' matching.mp3 : 対戦相手が決まった
    Win       ' win.mp3
    Lose      ' lose.mp3
End Enum

''' <summary>
''' 効果音とBGM。Resources\SE と Resources\BGM の mp3 を読み込んで NAudio で鳴らす。
''' BGM は起動時にランダムで1曲選び、終わったら同じ曲を繰り返す。
''' </summary>
Public NotInheritable Class SoundEngine
    Implements IDisposable

    Public Const SampleRate As Integer = 44100
    Private Shared ReadOnly MixFormat As WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)

    Private _out As WaveOutEvent
    Private _mixer As MixingSampleProvider
    Private _bgm As LoopingBgm
    Private ReadOnly _sfx As New Dictionary(Of Sfx, Single())
    Private _bgmVolume As Single = 0.6F
    Private _seVolume As Single = 0.8F

    Public ReadOnly Property Available As Boolean

    Public Sub New()
        Try
            _mixer = New MixingSampleProvider(MixFormat) With {.ReadFully = True}
            Dim seDir = Path.Combine(AppContext.BaseDirectory, "Resources", "SE")
            For Each s As Sfx In [Enum].GetValues(GetType(Sfx))
                Try
                    _sfx(s) = LoadSfx(Path.Combine(seDir, s.ToString().ToLowerInvariant() & ".mp3"))
                Catch ex As Exception
                    Debug.WriteLine($"SE load failed ({s}): {ex.Message}")
                End Try
            Next
            _out = New WaveOutEvent With {.DesiredLatency = 90, .NumberOfBuffers = 3}
            _out.Init(_mixer)
            _out.Play()
            Available = True
        Catch ex As Exception
            ' サウンドデバイスが無い環境でもゲームは遊べるようにする
            Available = False
            Debug.WriteLine("Sound disabled: " & ex.Message)
        End Try
    End Sub

    ''' <summary>あらゆる形式の入力を、ミキサーの形式（44.1kHz・ステレオ・float）にそろえる。</summary>
    Private Shared Function ToMixFormat(src As ISampleProvider) As ISampleProvider
        Dim p = src
        If p.WaveFormat.SampleRate <> SampleRate Then p = New WdlResamplingSampleProvider(p, SampleRate)
        If p.WaveFormat.Channels = 1 Then p = New MonoToStereoSampleProvider(p)
        Return p
    End Function

    Private Shared Function LoadSfx(path As String) As Single()
        Using reader As New Mp3FileReader(path)
            Dim sp = ToMixFormat(reader.ToSampleProvider())
            Dim data As New List(Of Single)
            Dim buf(SampleRate * 2 - 1) As Single
            Do
                Dim n = sp.Read(buf, 0, buf.Length)
                If n <= 0 Then Exit Do
                data.AddRange(New ArraySegment(Of Single)(buf, 0, n))
            Loop
            Return data.ToArray()
        End Using
    End Function

    ''' <summary>BGMの音量（0〜1）。</summary>
    Public Property BgmVolume As Single
        Get
            Return _bgmVolume
        End Get
        Set(value As Single)
            _bgmVolume = Math.Clamp(value, 0F, 1F)
            If _bgm IsNot Nothing Then _bgm.Gain = Curve(_bgmVolume) * 0.7F
        End Set
    End Property

    ''' <summary>効果音の音量（0〜1）。</summary>
    Public Property SeVolume As Single
        Get
            Return _seVolume
        End Get
        Set(value As Single)
            _seVolume = Math.Clamp(value, 0F, 1F)
        End Set
    End Property

    ' 耳の感覚に合わせて2乗カーブにする
    Private Shared Function Curve(v As Single) As Single
        Return v * v
    End Function

    Public Sub Play(s As Sfx)
        If Not Available Then Return
        Dim data As Single() = Nothing
        If Not _sfx.TryGetValue(s, data) Then Return
        Dim gain = Curve(_seVolume)
        If gain <= 0F Then Return
        _mixer.AddMixerInput(New OneShotProvider(data, MixFormat, gain))
    End Sub

    ''' <summary>BGM フォルダの mp3 からランダムに1曲選んで、ループ再生を始める。</summary>
    Public Sub StartBgm()
        If Not Available OrElse _bgm IsNot Nothing Then Return
        Try
            Dim dir = Path.Combine(AppContext.BaseDirectory, "Resources", "BGM")
            If Not Directory.Exists(dir) Then Return
            Dim files = Directory.GetFiles(dir, "*.mp3")
            If files.Length = 0 Then Return
            _bgm = New LoopingBgm(files(Random.Shared.Next(files.Length)))
            _bgm.Gain = Curve(_bgmVolume) * 0.7F
            _mixer.AddMixerInput(_bgm)
        Catch ex As Exception
            _bgm = Nothing
            Debug.WriteLine("BGM failed: " & ex.Message)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            _out?.Stop()
            _out?.Dispose()
            _bgm?.Close()
        Catch
        End Try
        _out = Nothing
    End Sub

    ''' <summary>一度だけ鳴らす音。</summary>
    Private NotInheritable Class OneShotProvider
        Implements ISampleProvider
        Private ReadOnly _data As Single()
        Private ReadOnly _gain As Single
        Private _pos As Integer
        Public ReadOnly Property WaveFormat As WaveFormat Implements ISampleProvider.WaveFormat

        Public Sub New(data As Single(), fmt As WaveFormat, gain As Single)
            _data = data : WaveFormat = fmt : _gain = gain
        End Sub

        Public Function Read(buffer() As Single, offset As Integer, count As Integer) As Integer Implements ISampleProvider.Read
            Dim n = Math.Min(count, _data.Length - _pos)
            For i = 0 To n - 1
                buffer(offset + i) = _data(_pos + i) * _gain
            Next
            _pos += n
            Return n
        End Function
    End Class

    ''' <summary>mp3 を読みながら鳴らし、終わったら最初から繰り返す（BGM）。</summary>
    Private NotInheritable Class LoopingBgm
        Implements ISampleProvider
        Private ReadOnly _path As String
        Private _reader As Mp3FileReader
        Private _src As ISampleProvider
        Private _cur As Single = 0F
        Public Property Gain As Single = 1.0F
        Public ReadOnly Property WaveFormat As WaveFormat Implements ISampleProvider.WaveFormat

        Public Sub New(path As String)
            _path = path
            WaveFormat = MixFormat
            Open()
        End Sub

        Private Sub Open()
            _reader?.Dispose()
            _reader = New Mp3FileReader(_path)
            _src = ToMixFormat(_reader.ToSampleProvider())
        End Sub

        Public Sub Close()
            _reader?.Dispose()
            _reader = Nothing
        End Sub

        Public Function Read(buffer() As Single, offset As Integer, count As Integer) As Integer Implements ISampleProvider.Read
            Dim total = 0
            Try
                Dim justReopened = False
                While total < count AndAlso _reader IsNot Nothing
                    Dim n = _src.Read(buffer, offset + total, count - total)
                    If n > 0 Then
                        total += n
                        justReopened = False
                    ElseIf justReopened Then
                        Exit While ' 中身が空のファイル
                    Else
                        Open() ' 曲の終わり: 同じ曲を最初から
                        justReopened = True
                    End If
                End While
            Catch ex As Exception
                Debug.WriteLine("BGM read error: " & ex.Message)
            End Try
            Dim target = Gain
            For i = 0 To total - 1
                ' 音量変更でプチノイズが出ないよう、少しずつ近づける
                _cur += (target - _cur) * 0.0003F
                buffer(offset + i) *= _cur
            Next
            For i = total To count - 1
                buffer(offset + i) = 0F
            Next
            Return count
        End Function
    End Class
End Class
