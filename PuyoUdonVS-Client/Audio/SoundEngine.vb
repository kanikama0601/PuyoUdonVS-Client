Imports NAudio.Wave
Imports NAudio.Wave.SampleProviders

Public Enum Sfx
    Move
    Rotate
    Land
    HardDrop
    Erase1
    Erase2
    Erase3
    Erase4
    Erase5
    Erase6
    Erase7
    GarbageFall
    Attack
    Beep
    Go
    Win
    Lose
    Click
    Join
End Enum

''' <summary>
''' 効果音とBGM。音声データはすべて起動時にプログラムで合成する（外部ファイル不要）。
''' NAudio の WaveOutEvent で再生し、ミキサーで重ねて鳴らす。
''' </summary>
Public NotInheritable Class SoundEngine
    Implements IDisposable

    Public Const SampleRate As Integer = 44100

    Private _out As WaveOutEvent
    Private _mixer As MixingSampleProvider
    Private _master As VolumeSampleProvider
    Private _bgm As LoopProvider
    Private ReadOnly _sfx As New Dictionary(Of Sfx, Single())
    Private _volume As Single = 0.6F
    Private _muted As Boolean
    Private _bgmOn As Boolean

    Public ReadOnly Property Available As Boolean

    Public Sub New()
        Try
            Dim fmt = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1)
            _mixer = New MixingSampleProvider(fmt) With {.ReadFully = True}
            _master = New VolumeSampleProvider(_mixer)
            _bgm = New LoopProvider(Synth.RenderBgm(), fmt) With {.Gain = 0.55F}
            For Each s As Sfx In [Enum].GetValues(GetType(Sfx))
                _sfx(s) = Synth.RenderSfx(s)
            Next
            _out = New WaveOutEvent With {.DesiredLatency = 90, .NumberOfBuffers = 3}
            _out.Init(_master)
            ApplyVolume()
            _out.Play()
            Available = True
        Catch ex As Exception
            ' サウンドデバイスが無い環境でもゲームは遊べるようにする
            Available = False
            Debug.WriteLine("Sound disabled: " & ex.Message)
        End Try
    End Sub

    ''' <summary>全体の音量（0〜1）。</summary>
    Public Property Volume As Single
        Get
            Return _volume
        End Get
        Set(value As Single)
            _volume = Math.Clamp(value, 0F, 1F)
            ApplyVolume()
        End Set
    End Property

    Public Property Muted As Boolean
        Get
            Return _muted
        End Get
        Set(value As Boolean)
            _muted = value
            ApplyVolume()
        End Set
    End Property

    Private Sub ApplyVolume()
        If _master Is Nothing Then Return
        ' 耳の感覚に合わせて2乗カーブにする
        _master.Volume = If(_muted, 0F, _volume * _volume)
    End Sub

    Public Sub Play(s As Sfx, Optional gain As Single = 1.0F)
        If Not Available Then Return
        _mixer.AddMixerInput(New OneShotProvider(_sfx(s), _mixer.WaveFormat, gain))
    End Sub

    Public Sub PlayErase(chain As Integer)
        Play(CType(Sfx.Erase1 + Math.Clamp(chain, 1, 7) - 1, Sfx))
    End Sub

    Public Property BgmPlaying As Boolean
        Get
            Return _bgmOn
        End Get
        Set(value As Boolean)
            If Not Available Then Return
            If value = _bgmOn Then Return
            _bgmOn = value
            If value Then
                _mixer.AddMixerInput(_bgm)
            Else
                _mixer.RemoveMixerInput(_bgm)
            End If
        End Set
    End Property

    ''' <summary>BGMの音量だけを変える（結果画面で少し下げるなど）。</summary>
    Public Sub SetBgmGain(g As Single)
        If _bgm IsNot Nothing Then _bgm.Gain = g
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            _out?.Stop()
            _out?.Dispose()
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

    ''' <summary>繰り返し鳴らす音（BGM）。</summary>
    Private NotInheritable Class LoopProvider
        Implements ISampleProvider
        Private ReadOnly _data As Single()
        Private _pos As Integer
        Public Property Gain As Single = 1.0F
        Private _cur As Single = 0F
        Public ReadOnly Property WaveFormat As WaveFormat Implements ISampleProvider.WaveFormat

        Public Sub New(data As Single(), fmt As WaveFormat)
            _data = data : WaveFormat = fmt
        End Sub

        Public Function Read(buffer() As Single, offset As Integer, count As Integer) As Integer Implements ISampleProvider.Read
            Dim target = Gain
            For i = 0 To count - 1
                ' 音量変更でプチノイズが出ないよう、少しずつ近づける
                _cur += (target - _cur) * 0.0005F
                buffer(offset + i) = _data(_pos) * _cur
                _pos += 1
                If _pos >= _data.Length Then _pos = 0
            Next
            Return count
        End Function
    End Class
End Class
