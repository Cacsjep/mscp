using AutoExporter.Background;
using Xunit;

namespace AutoExporter.Tests
{
    public class HelperProgressParserTests
    {
        [Fact]
        public void Progress_line_with_all_fields()
        {
            var r = HelperProgressParser.Parse("PROGRESS cameraIdx=2 pct=47 name=Lobby Camera");
            Assert.Equal(HelperProgressParser.HelperLine.LineKind.Progress, r.Kind);
            Assert.Equal(2, r.CameraIndex);
            Assert.Equal(47, r.Percent);
            Assert.Equal("Lobby Camera", r.CameraName);
        }

        [Fact]
        public void Name_field_captures_remaining_line_including_spaces_and_equals()
        {
            var r = HelperProgressParser.Parse("PROGRESS cameraIdx=0 pct=100 name=Camera 01 - Parking East = main");
            Assert.Equal(HelperProgressParser.HelperLine.LineKind.Progress, r.Kind);
            Assert.Equal(0, r.CameraIndex);
            Assert.Equal(100, r.Percent);
            Assert.Equal("Camera 01 - Parking East = main", r.CameraName);
        }

        [Fact]
        public void Empty_name_value_is_handled()
        {
            var r = HelperProgressParser.Parse("PROGRESS cameraIdx=3 pct=10 name=");
            Assert.Equal(HelperProgressParser.HelperLine.LineKind.Progress, r.Kind);
            Assert.Equal(3, r.CameraIndex);
            Assert.Equal(10, r.Percent);
            Assert.Equal("", r.CameraName);
        }

        [Theory]
        [InlineData("")]
        [InlineData("INIT done")]
        [InlineData("ERR SDK init: foo")]
        [InlineData("DONE bytes=12345 cameras=4")]
        public void Non_PROGRESS_lines_are_Info_kind(string line)
        {
            var r = HelperProgressParser.Parse(line);
            Assert.Equal(HelperProgressParser.HelperLine.LineKind.Info, r.Kind);
            Assert.Equal(line ?? "", r.Raw);
        }

        [Fact]
        public void Null_line_is_Info_with_empty_raw()
        {
            var r = HelperProgressParser.Parse(null);
            Assert.Equal(HelperProgressParser.HelperLine.LineKind.Info, r.Kind);
            Assert.Equal("", r.Raw);
        }

        [Fact]
        public void Malformed_progress_line_does_not_throw()
        {
            // Missing numeric value
            var r = HelperProgressParser.Parse("PROGRESS cameraIdx=abc pct=xyz name=Cam");
            Assert.Equal(HelperProgressParser.HelperLine.LineKind.Progress, r.Kind);
            // Failed parses leave the int default of 0.
            Assert.Equal(0, r.CameraIndex);
            Assert.Equal(0, r.Percent);
            Assert.Equal("Cam", r.CameraName);
        }

        [Fact]
        public void Unknown_keys_are_ignored()
        {
            var r = HelperProgressParser.Parse("PROGRESS cameraIdx=1 pct=50 weird=42 name=Cam");
            Assert.Equal(1, r.CameraIndex);
            Assert.Equal(50, r.Percent);
            Assert.Equal("Cam", r.CameraName);
        }
    }
}
