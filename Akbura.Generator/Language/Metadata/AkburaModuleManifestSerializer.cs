using Akbura.Language.Symbols;
using Akbura.Pools;
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Akbura.Language;

internal static class AkburaModuleManifestSerializer
{
    private const string RootElementName = "akburaModule";
    private const string SourceElementName = "source";
    private const string DeclarationElementName = "declaration";
    private const string ComponentElementName = "component";
    private const string AkcssUtilityElementName = "akcssUtility";
    private const string ParameterElementName = "parameter";
    private const string InjectElementName = "inject";

    public static void Write(Stream stream, AkburaModuleManifest manifest)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };

        using var writer = XmlWriter.Create(stream, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement(RootElementName);
        writer.WriteAttributeString(
            "formatVersion",
            manifest.FormatVersion.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("assemblyName", manifest.AssemblyName);

        foreach (var source in manifest.Sources)
        {
            WriteSource(writer, source);
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    public static AkburaModuleManifest Read(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };

        using var reader = XmlReader.Create(stream, settings);
        reader.MoveToContent();
        if (reader.NodeType != XmlNodeType.Element ||
            reader.Name != RootElementName)
        {
            throw new InvalidDataException($"Expected '{RootElementName}' root element.");
        }

        var formatVersion = ReadRequiredInt32(reader, "formatVersion");
        if (formatVersion > AkburaModuleManifest.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Akbura module format version {formatVersion} is newer than supported version " +
                $"{AkburaModuleManifest.CurrentFormatVersion}.");
        }

        var assemblyName = reader.GetAttribute("assemblyName") ?? string.Empty;
        using var sources = ImmutableArrayBuilder<AkburaModuleSource>.Rent();

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement(RootElementName);
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                if (reader.Name == SourceElementName)
                {
                    sources.Add(ReadSource(reader));
                }
                else
                {
                    reader.Skip();
                }
            }

            reader.ReadEndElement();
        }

        return new AkburaModuleManifest(
            formatVersion,
            assemblyName,
            sources.ToImmutable());
    }

    public static bool TryRead(
        Assembly assembly,
        out AkburaModuleManifest? manifest)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        using var stream = assembly.GetManifestResourceStream(AkburaModuleManifest.ResourceName);
        if (stream == null)
        {
            manifest = null;
            return false;
        }

        manifest = Read(stream);
        return true;
    }

    public static Stream OpenSource(
        Assembly assembly,
        AkburaModuleSource source)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return assembly.GetManifestResourceStream(source.SourceCodePath)
            ?? throw new InvalidDataException(
                $"Assembly '{assembly.FullName}' does not contain Akbura source resource " +
                $"'{source.SourceCodePath}'.");
    }

    private static void WriteSource(XmlWriter writer, AkburaModuleSource source)
    {
        writer.WriteStartElement(SourceElementName);
        writer.WriteAttributeString("path", source.SourceCodePath);
        writer.WriteAttributeString("kind", source.Kind.ToString());

        foreach (var declaration in source.Declarations)
        {
            WriteDeclaration(writer, declaration);
        }

        writer.WriteEndElement();
    }

    private static void WriteDeclaration(
        XmlWriter writer,
        AkburaModuleDeclaration declaration)
    {
        writer.WriteStartElement(DeclarationElementName);
        writer.WriteAttributeString("kind", declaration.Kind.ToString());
        writer.WriteAttributeString("name", declaration.Name);
        if (declaration.MetadataName != null)
        {
            writer.WriteAttributeString("metadataName", declaration.MetadataName);
        }

        writer.WriteAttributeString(
            "start",
            declaration.SourceStart.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "length",
            declaration.SourceLength.ToString(CultureInfo.InvariantCulture));

        if (declaration.AkcssUtility != null)
        {
            WriteAkcssUtility(writer, declaration.AkcssUtility);
        }

        if (declaration.Component != null)
        {
            WriteComponent(writer, declaration.Component);
        }

        foreach (var child in declaration.Children)
        {
            WriteDeclaration(writer, child);
        }

        writer.WriteEndElement();
    }

    private static void WriteComponent(
        XmlWriter writer,
        AkburaModuleComponent component)
    {
        writer.WriteStartElement(ComponentElementName);
        writer.WriteAttributeString("baseType", component.BaseTypeName);
        writer.WriteAttributeString(
            "parameterCount",
            component.ParameterCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "injectCount",
            component.InjectedServiceCount.ToString(CultureInfo.InvariantCulture));

        foreach (var parameter in component.Parameters)
        {
            writer.WriteStartElement(ParameterElementName);
            writer.WriteAttributeString(
                "ordinal",
                parameter.Ordinal.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("name", parameter.Name);
            writer.WriteAttributeString("type", parameter.TypeName);
            writer.WriteAttributeString("binding", parameter.BindingKind.ToString());
            writer.WriteAttributeString(
                "hasDefaultValue",
                XmlConvert.ToString(parameter.HasDefaultValue));
            writer.WriteAttributeString(
                "start",
                parameter.SourceStart.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "length",
                parameter.SourceLength.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        foreach (var injectedService in component.InjectedServices)
        {
            writer.WriteStartElement(InjectElementName);
            writer.WriteAttributeString(
                "ordinal",
                injectedService.Ordinal.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("name", injectedService.Name);
            writer.WriteAttributeString("type", injectedService.TypeName);
            writer.WriteAttributeString(
                "start",
                injectedService.SourceStart.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "length",
                injectedService.SourceLength.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteAkcssUtility(
        XmlWriter writer,
        AkburaModuleAkcssUtility utility)
    {
        writer.WriteStartElement(AkcssUtilityElementName);
        if (utility.TargetTypeName != null)
        {
            writer.WriteAttributeString("targetType", utility.TargetTypeName);
        }

        writer.WriteAttributeString(
            "parameterCount",
            utility.ParameterCount.ToString(CultureInfo.InvariantCulture));

        foreach (var parameter in utility.Parameters)
        {
            WriteAkcssUtilityParameter(writer, parameter);
        }

        writer.WriteEndElement();
    }

    private static void WriteAkcssUtilityParameter(
        XmlWriter writer,
        AkburaModuleAkcssUtilityParameter parameter)
    {
        writer.WriteStartElement(ParameterElementName);
        writer.WriteAttributeString(
            "ordinal",
            parameter.Ordinal.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("name", parameter.Name);
        writer.WriteAttributeString("type", parameter.TypeName);
        writer.WriteAttributeString(
            "start",
            parameter.SourceStart.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "length",
            parameter.SourceLength.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    private static AkburaModuleSource ReadSource(XmlReader reader)
    {
        var sourceCodePath = ReadRequiredAttribute(reader, "path");
        var kindText = ReadRequiredAttribute(reader, "kind");
        if (!Enum.TryParse(kindText, ignoreCase: false, out AkburaModuleSourceKind kind))
        {
            throw new InvalidDataException($"Unknown Akbura module source kind '{kindText}'.");
        }

        using var declarations = ImmutableArrayBuilder<AkburaModuleDeclaration>.Rent();
        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement(SourceElementName);
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                if (reader.Name == DeclarationElementName)
                {
                    declarations.Add(ReadDeclaration(reader));
                }
                else
                {
                    reader.Skip();
                }
            }

            reader.ReadEndElement();
        }

        return new AkburaModuleSource(
            sourceCodePath,
            kind,
            declarations.ToImmutable());
    }

    private static AkburaModuleDeclaration ReadDeclaration(XmlReader reader)
    {
        var kindText = ReadRequiredAttribute(reader, "kind");
        if (!Enum.TryParse(kindText, ignoreCase: false, out DeclarationKind kind))
        {
            throw new InvalidDataException($"Unknown Akbura declaration kind '{kindText}'.");
        }

        var name = reader.GetAttribute("name") ?? string.Empty;
        var metadataName = reader.GetAttribute("metadataName");
        var sourceStart = ReadRequiredInt32(reader, "start");
        var sourceLength = ReadRequiredInt32(reader, "length");
        using var children = ImmutableArrayBuilder<AkburaModuleDeclaration>.Rent();
        AkburaModuleAkcssUtility? akcssUtility = null;
        AkburaModuleComponent? component = null;

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement(DeclarationElementName);
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                if (reader.Name == DeclarationElementName)
                {
                    children.Add(ReadDeclaration(reader));
                }
                else if (reader.Name == AkcssUtilityElementName)
                {
                    if (akcssUtility != null)
                    {
                        throw new InvalidDataException(
                            "An AKCSS utility declaration contains more than one utility signature.");
                    }

                    akcssUtility = ReadAkcssUtility(reader);
                }
                else if (reader.Name == ComponentElementName)
                {
                    if (component != null)
                    {
                        throw new InvalidDataException(
                            "A component declaration contains more than one component signature.");
                    }

                    component = ReadComponent(reader);
                }
                else
                {
                    reader.Skip();
                }
            }

            reader.ReadEndElement();
        }

        return new AkburaModuleDeclaration(
            kind,
            name,
            metadataName,
            sourceStart,
            sourceLength,
            children.ToImmutable(),
            akcssUtility,
            component);
    }

    private static AkburaModuleComponent ReadComponent(XmlReader reader)
    {
        var baseTypeName = ReadRequiredAttribute(reader, "baseType");
        var expectedParameterCount = ReadRequiredInt32(reader, "parameterCount");
        var expectedInjectCount = ReadRequiredInt32(reader, "injectCount");
        using var parameters = ImmutableArrayBuilder<AkburaModuleComponentParameter>.Rent(
            expectedParameterCount);
        using var injectedServices = ImmutableArrayBuilder<AkburaModuleComponentInject>.Rent(
            expectedInjectCount);

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement(ComponentElementName);
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                if (reader.Name == ParameterElementName)
                {
                    var parameter = ReadComponentParameter(reader);
                    if (parameter.Ordinal != parameters.Count)
                    {
                        throw new InvalidDataException(
                            $"Component parameter ordinal {parameter.Ordinal} is out of order.");
                    }

                    parameters.Add(parameter);
                }
                else if (reader.Name == InjectElementName)
                {
                    var injectedService = ReadComponentInject(reader);
                    if (injectedService.Ordinal != injectedServices.Count)
                    {
                        throw new InvalidDataException(
                            $"Injected service ordinal {injectedService.Ordinal} is out of order.");
                    }

                    injectedServices.Add(injectedService);
                }
                else
                {
                    reader.Skip();
                }
            }

            reader.ReadEndElement();
        }

        if (expectedParameterCount != parameters.Count)
        {
            throw new InvalidDataException(
                $"Component declares {expectedParameterCount} parameters but contains {parameters.Count}.");
        }

        if (expectedInjectCount != injectedServices.Count)
        {
            throw new InvalidDataException(
                $"Component declares {expectedInjectCount} injected services but contains {injectedServices.Count}.");
        }

        return new AkburaModuleComponent(
            baseTypeName,
            parameters.ToImmutable(),
            injectedServices.ToImmutable());
    }

    private static AkburaModuleComponentParameter ReadComponentParameter(
        XmlReader reader)
    {
        var ordinal = ReadRequiredInt32(reader, "ordinal");
        var name = ReadRequiredAttribute(reader, "name");
        var typeName = ReadRequiredAttribute(reader, "type");
        var bindingText = ReadRequiredAttribute(reader, "binding");
        if (!Enum.TryParse(bindingText, ignoreCase: false, out ParamBindingKind bindingKind))
        {
            throw new InvalidDataException(
                $"Unknown component parameter binding kind '{bindingText}'.");
        }

        var hasDefaultValue = ReadRequiredBoolean(reader, "hasDefaultValue");
        var sourceStart = ReadRequiredInt32(reader, "start");
        var sourceLength = ReadRequiredInt32(reader, "length");
        reader.Skip();

        return new AkburaModuleComponentParameter(
            ordinal,
            name,
            typeName,
            bindingKind,
            hasDefaultValue,
            sourceStart,
            sourceLength);
    }

    private static AkburaModuleComponentInject ReadComponentInject(
        XmlReader reader)
    {
        var ordinal = ReadRequiredInt32(reader, "ordinal");
        var name = ReadRequiredAttribute(reader, "name");
        var typeName = ReadRequiredAttribute(reader, "type");
        var sourceStart = ReadRequiredInt32(reader, "start");
        var sourceLength = ReadRequiredInt32(reader, "length");
        reader.Skip();

        return new AkburaModuleComponentInject(
            ordinal,
            name,
            typeName,
            sourceStart,
            sourceLength);
    }

    private static AkburaModuleAkcssUtility ReadAkcssUtility(
        XmlReader reader)
    {
        var targetTypeName = reader.GetAttribute("targetType");
        var expectedParameterCount = ReadRequiredInt32(reader, "parameterCount");
        using var parameters = ImmutableArrayBuilder<AkburaModuleAkcssUtilityParameter>.Rent(
            expectedParameterCount);

        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement(AkcssUtilityElementName);
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                if (reader.Name == ParameterElementName)
                {
                    var parameter = ReadAkcssUtilityParameter(reader);
                    if (parameter.Ordinal != parameters.Count)
                    {
                        throw new InvalidDataException(
                            $"AKCSS utility parameter ordinal {parameter.Ordinal} is out of order.");
                    }

                    parameters.Add(parameter);
                }
                else
                {
                    reader.Skip();
                }
            }

            reader.ReadEndElement();
        }

        if (expectedParameterCount != parameters.Count)
        {
            throw new InvalidDataException(
                $"AKCSS utility declares {expectedParameterCount} parameters but contains {parameters.Count}.");
        }

        return new AkburaModuleAkcssUtility(
            targetTypeName,
            parameters.ToImmutable());
    }

    private static AkburaModuleAkcssUtilityParameter ReadAkcssUtilityParameter(
        XmlReader reader)
    {
        var ordinal = ReadRequiredInt32(reader, "ordinal");
        var name = ReadRequiredAttribute(reader, "name");
        var typeName = ReadRequiredAttribute(reader, "type");
        var sourceStart = ReadRequiredInt32(reader, "start");
        var sourceLength = ReadRequiredInt32(reader, "length");
        reader.Skip();

        return new AkburaModuleAkcssUtilityParameter(
            ordinal,
            name,
            typeName,
            sourceStart,
            sourceLength);
    }

    private static string ReadRequiredAttribute(XmlReader reader, string name)
    {
        return reader.GetAttribute(name)
            ?? throw new InvalidDataException(
                $"Element '{reader.Name}' is missing required attribute '{name}'.");
    }

    private static int ReadRequiredInt32(XmlReader reader, string name)
    {
        var value = ReadRequiredAttribute(reader, name);
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var result) ||
            result < 0)
        {
            throw new InvalidDataException(
                $"Attribute '{name}' on element '{reader.Name}' is not a non-negative integer.");
        }

        return result;
    }

    private static bool ReadRequiredBoolean(XmlReader reader, string name)
    {
        var value = ReadRequiredAttribute(reader, name);
        try
        {
            return XmlConvert.ToBoolean(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                $"Attribute '{name}' on element '{reader.Name}' is not a Boolean.",
                exception);
        }
    }
}
