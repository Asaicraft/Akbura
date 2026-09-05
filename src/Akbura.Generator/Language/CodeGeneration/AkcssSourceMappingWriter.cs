using Akbura.Language.Syntax;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct AkcssSourceMappingWriter
{
    private readonly CodeWriter _writer;
    private readonly AkcssGenerationSourceMap _sourceMap;

    public AkcssSourceMappingWriter(CodeWriter writer, AkcssGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);

        _writer = writer!;
        _sourceMap = sourceMap!;
    }

    public SourceMappingToken WriteStart(AkburaSyntax? syntax, int valueOffset = 0)
    {
        if (syntax == null || !_sourceMap.TryGetLineDirective(syntax, out var span, out var path))
        {
            return default;
        }

        return SourceMappingWriter.WriteStartCore(_writer, span, path, valueOffset);
    }
}
