using System.Text.Json;
using VideoNote.Server.Media;
namespace VideoNote.Server.Tests.Media;

public sealed class SubtitleSelectionTests
{
    [Theory]
    [InlineData("subrip")]
    [InlineData("ass")]
    [InlineData("ssa")]
    [InlineData("webvtt")]
    [InlineData("mov_text")]
    [InlineData("text")]
    public void Text_track_after_bitmap_is_selected_by_absolute_stream_index(string codec)
    {
        var json = JsonSerializer.Serialize(new
        {
            format = new { duration = "10.0" },
            streams = new[]
            {
                new { index = 0, codec_type = "video", codec_name = "h264" },
                new { index = 1, codec_type = "audio", codec_name = "aac" },
                new { index = 2, codec_type = "subtitle", codec_name = "hdmv_pgs_subtitle" },
                new { index = 4, codec_type = "subtitle", codec_name = codec }
            }
        });
        var info = FfmpegService.ParseProbe(json);
        Assert.Equal(codec, info.SubtitleCodec);
        Assert.Equal(4, info.SubtitleStreamIndex);
        Assert.True(info.HasVideo && info.HasAudio);
    }

    [Theory]
    [InlineData("hdmv_pgs_subtitle")]
    [InlineData("dvd_subtitle")]
    [InlineData("unknown")]
    public void No_extractable_text_track_allows_transcription_fallback(string codec)
    {
        var json = JsonSerializer.Serialize(new
        {
            format = new { duration = "10.0" },
            streams = new[] { new { index = 2, codec_type = "subtitle", codec_name = codec } }
        });
        var info = FfmpegService.ParseProbe(json);
        Assert.Null(info.SubtitleCodec);
        Assert.Null(info.SubtitleStreamIndex);
    }
}
