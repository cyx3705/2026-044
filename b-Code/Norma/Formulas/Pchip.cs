namespace Norma.Formulas;

/// <summary>
/// 单调保形的分段三次 Hermite 插值（Fritsch–Carlson/PCHIP）。
/// 节点处严格取原值，节点之间不超调；输入超出节点范围返回 NaN，由调用方判为「无定义」。
/// </summary>
internal sealed class Pchip
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _m;

    public Pchip(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 2)
            throw new ArgumentException("PCHIP 至少需要两个节点");

        var ordered = points.OrderBy(point => point.X).ToArray();
        for (var i = 1; i < ordered.Length; i++)
            if (!(ordered[i].X > ordered[i - 1].X))
                throw new ArgumentException($"PCHIP 的 X 必须严格递增：{ordered[i - 1].X}、{ordered[i].X}");

        var n = ordered.Length;
        _x = new double[n];
        _y = new double[n];
        for (var i = 0; i < n; i++)
        {
            _x[i] = ordered[i].X;
            _y[i] = ordered[i].Y;
            if (!double.IsFinite(_y[i]))
                throw new ArgumentException($"PCHIP 的 Y 必须是有限值（X={_x[i]}）");
        }

        var h = new double[n - 1];
        var delta = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
        {
            h[i] = _x[i + 1] - _x[i];
            delta[i] = (_y[i + 1] - _y[i]) / h[i];
        }

        _m = new double[n];
        if (n == 2)
        {
            _m[0] = _m[1] = delta[0];
            return;
        }

        _m[0] = Endpoint(h[0], h[1], delta[0], delta[1]);
        _m[n - 1] = Endpoint(h[n - 2], h[n - 3], delta[n - 2], delta[n - 3]);
        for (var i = 1; i < n - 1; i++)
        {
            if (delta[i - 1] * delta[i] <= 0)
            {
                _m[i] = 0;
                continue;
            }

            var w1 = 2 * h[i] + h[i - 1];
            var w2 = h[i] + 2 * h[i - 1];
            _m[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
        }
    }

    private static double Endpoint(double h0, double h1, double d0, double d1)
    {
        var m = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
        if (Math.Sign(m) != Math.Sign(d0))
            return 0;
        return Math.Sign(d0) != Math.Sign(d1) && Math.Abs(m) > 3 * Math.Abs(d0) ? 3 * d0 : m;
    }

    public double MinX => _x[0];

    public double MaxX => _x[^1];

    public double MinY => _y[0];

    public double MaxY => _y[^1];

    public bool CoversX(double x) => x >= _x[0] && x <= _x[^1];

    public bool CoversY(double y) => y >= _y[0] && y <= _y[^1];

    /// <summary>在节点之间求值；x 超出节点范围返回 NaN。</summary>
    public double Evaluate(double x)
    {
        if (!CoversX(x))
            return double.NaN;
        if (x == _x[^1])
            return _y[^1];

        var i = Interval(x);
        var h = _x[i + 1] - _x[i];
        var t = (x - _x[i]) / h;
        var t2 = t * t;
        var t3 = t2 * t;
        var h00 = 2 * t3 - 3 * t2 + 1;
        var h10 = t3 - 2 * t2 + t;
        var h01 = -2 * t3 + 3 * t2;
        var h11 = t3 - t2;
        return h00 * _y[i] + h10 * h * _m[i] + h01 * _y[i + 1] + h11 * h * _m[i + 1];
    }

    /// <summary>
    /// 单调曲线的反解：给定 Y 在节点范围内，二分求 X。Y 超出范围返回 NaN。
    /// 平台段返回一个可行 X，不做额外猜测。
    /// </summary>
    public double Invert(double y)
    {
        if (!CoversY(y))
            return double.NaN;
        if (y <= _y[0])
            return _x[0];
        if (y >= _y[^1])
            return _x[^1];

        var lo = _x[0];
        var hi = _x[^1];
        for (var i = 0; i < 120; i++)
        {
            var mid = (lo + hi) / 2;
            if (Evaluate(mid) < y)
                lo = mid;
            else
                hi = mid;
        }

        return (lo + hi) / 2;
    }

    private int Interval(double x)
    {
        var lo = 0;
        var hi = _x.Length - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (_x[mid] <= x)
                lo = mid;
            else
                hi = mid;
        }

        return lo;
    }
}
