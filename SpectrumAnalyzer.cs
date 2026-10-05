namespace DynamicIsland;

sealed class SpectrumAnalyzer
{
    public const int Bands = 40;

    const double MinHz = 45, MaxHz = 14000;
    const double TiltDb = 2;
    const int MinWindow = 1024;
    const double WindowSeconds = 0.04;
    const double UsableBandwidth = 0.45;

    readonly int _size;
    readonly float[] _ring;
    readonly double[] _window, _re, _im, _power;
    readonly double[] _edges = new double[Bands + 1];
    readonly double[] _tilt = new double[Bands];
    int _head;

    public SpectrumAnalyzer(int rate)
    {
        _size = MinWindow;
        while (_size < rate * WindowSeconds) _size <<= 1;

        _ring = new float[_size];
        _window = new double[_size];
        _re = new double[_size];
        _im = new double[_size];
        _power = new double[_size / 2];
        for (int i = 0; i < _size; i++) _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (_size - 1));

        double top = Math.Min(MaxHz, rate * UsableBandwidth);
        for (int b = 0; b <= Bands; b++)
            _edges[b] = MinHz * Math.Pow(top / MinHz, (double)b / Bands) * _size / rate;
        for (int b = 0; b < Bands; b++)
        {
            double centerHz = Math.Sqrt(_edges[b] * _edges[b + 1]) * rate / _size;
            _tilt[b] = Math.Pow(10, TiltDb * Math.Log2(centerHz / 1000) / 10);
        }
    }

    public void Push(float[] samples, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _ring[_head] = samples[i];
            _head = (_head + 1) & (_size - 1);
        }
    }

    public void Analyze(float[] bands)
    {
        for (int i = 0; i < _size; i++)
        {
            _re[i] = _ring[(_head + i) & (_size - 1)] * _window[i];
            _im[i] = 0;
        }
        Fft(_re, _im);

        double norm = 16.0 / ((double)_size * _size);
        for (int k = 0; k < _power.Length; k++) _power[k] = (_re[k] * _re[k] + _im[k] * _im[k]) * norm;

        for (int b = 0; b < Bands; b++)
        {
            double lo = _edges[b], hi = _edges[b + 1], sum = 0;
            int last = Math.Min((int)(hi + 0.5), _power.Length - 1);
            for (int k = (int)(lo + 0.5); k <= last; k++)
            {
                double overlap = Math.Min(hi, k + 0.5) - Math.Max(lo, k - 0.5);
                if (overlap > 0) sum += overlap * _power[k];
            }
            bands[b] = (float)(sum * _tilt[b]);
        }
    }

    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len / 2;
            double angle = -2 * Math.PI / len;
            double stepR = Math.Cos(angle), stepI = Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                double wr = 1, wi = 0;
                for (int k = 0; k < half; k++)
                {
                    int a = i + k, b = a + half;
                    double xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - xr;
                    im[b] = im[a] - xi;
                    re[a] += xr;
                    im[a] += xi;
                    double next = wr * stepR - wi * stepI;
                    wi = wr * stepI + wi * stepR;
                    wr = next;
                }
            }
        }
    }
}
