using System.Globalization;
using System.IO;

namespace EyeCamXR
{
    /// <summary>
    /// cursor_log.csv / calib_log.csv writer: columns 0-4 are the exact
    /// phantom/laptop schema (time,grid_x,grid_y,luminance,flicker_on), plus
    /// frame_dt for the flicker-purity analysis. Flushed per row: the phantom
    /// and the reconstruction both tail these live (0.15 s liveness window).
    /// </summary>
    public sealed class StimulusLogger : System.IDisposable
    {
        readonly StreamWriter _w;
        int _lastFlicker = -1;

        public StimulusLogger(string path)
        {
            _w = new StreamWriter(path, false) { AutoFlush = true };
            _w.Write("time,grid_x,grid_y,luminance,flicker_on,frame_dt\n");
        }

        public void Row(double t, int gx, int gy, double lum, bool flickerOn,
                        double frameDt)
        {
            _w.Write(t.ToString("F6", CultureInfo.InvariantCulture));
            _w.Write(',');
            _w.Write(gx);
            _w.Write(',');
            _w.Write(gy);
            _w.Write(',');
            _w.Write(lum.ToString("F4", CultureInfo.InvariantCulture));
            _w.Write(flickerOn ? ",1," : ",0,");
            _w.Write(frameDt.ToString("F6", CultureInfo.InvariantCulture));
            _w.Write("\n");
            _lastFlicker = flickerOn ? 1 : 0;
        }

        public bool FlickerChanged(bool on) => (_lastFlicker == 1) != on;

        public void Dispose() => _w.Dispose();
    }
}
