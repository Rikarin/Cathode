namespace Cathode.Dsp.Parameters;

/// <summary>
///     Builds <see cref="ParamDescriptor.TypeLabels" /> for blocks whose types have different pages: a parameter shown
///     only on some types, or named differently on one.
/// </summary>
public static class TypeLabelling {
    /// <summary>
    ///     <paramref name="p" />, shown only on <paramref name="types" /> (of <paramref name="typeCount" />).
    /// </summary>
    public static ParamDescriptor ShownOn(this ParamDescriptor p, int typeCount, params int[] types) {
        var labels = Labels(p, typeCount);
        for (var t = 0; t < typeCount; t++) {
            if (!types.Contains(t)) {
                labels[t] = string.Empty;
            }
        }

        return p with { TypeLabels = labels };
    }

    /// <summary>
    ///     <paramref name="p" />, hidden on <paramref name="types" /> (of <paramref name="typeCount" />).
    /// </summary>
    public static ParamDescriptor HiddenOn(this ParamDescriptor p, int typeCount, params int[] types) {
        var labels = Labels(p, typeCount);
        foreach (var t in types) {
            labels[t] = string.Empty;
        }

        return p with { TypeLabels = labels };
    }

    /// <summary><paramref name="p" />, called <paramref name="name" /> on <paramref name="type" />.</summary>
    public static ParamDescriptor RenamedOn(this ParamDescriptor p, int typeCount, int type, string name) {
        var labels = Labels(p, typeCount);
        if (labels[type] != string.Empty) {
            labels[type] = name;
        }

        return p with { TypeLabels = labels };
    }

    static string[] Labels(ParamDescriptor p, int typeCount) =>
        p.TypeLabels is { } existing ? [..existing] : [..Enumerable.Repeat(p.Name, typeCount)];
}
