using Akbura.Language;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Akbura.Benchmarks;

internal static class Program
{
	public static void Main(string[] args)
	{
#if ENABLE_QUICK_SCAN_BENCHMARK
		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
#else
		Console.WriteLine("Quick scan benchmark is disabled. Run with /p:EnableQuickScanBenchmark=true.");
#endif
	}
}

#if ENABLE_QUICK_SCAN_BENCHMARK
[Config(typeof(QuickScanBenchmarkConfig))]
[MemoryDiagnoser]
public class LexerQuickScannerBenchmarks
{
	private static readonly QuickScanLexerFactory LexerFactory = QuickScanLexerFactory.Create();

	private object _source = null!;

	[Params(80)]
	public int Repetitions { get; set; }

	[GlobalSetup]
	public void GlobalSetup()
	{
		_source = LexerFactory.CreateSourceText(BuildComponentCorpus(Repetitions));
	}

	[Benchmark(Baseline = true)]
	public int ParseWithQuickScannerDisabled()
	{
		return Parse(enableQuickScanner: false);
	}

	[Benchmark]
	public int ParseWithQuickScannerEnabled()
	{
		return Parse(enableQuickScanner: true);
	}

	private int Parse(bool enableQuickScanner)
	{
		using var lexer = LexerFactory.CreateLexer(_source, enableQuickScanner);
		using var parser = new Parser(lexer, default);
		var syntax = parser.ParseCompilationUnit();

		return syntax.FullWidth;
	}

	private static string BuildComponentCorpus(int repetitions)
	{
		var builder = new StringBuilder();

		builder.AppendLine("using System;");
		builder.AppendLine("namespace Benchmark.App;");
		builder.AppendLine();
		builder.AppendLine("@akcss {");
		builder.AppendLine("\t.card { Padding: 12; MarginLeft: 4; MarginTop: 2; }");
		builder.AppendLine("\t@utilities {");
		builder.AppendLine("\t\t.w-(double width) { Width: width * Spacing; }");
		builder.AppendLine("\t\t.gap-(int value) { RowGap: value * Spacing; }");
		builder.AppendLine("\t}");
		builder.AppendLine("}");
		builder.AppendLine();
		builder.AppendLine("state int count = 0;");
		builder.AppendLine("state bool isOpen = false;");
		builder.AppendLine();

		for (var i = 0; i < repetitions; i++)
		{
			builder.AppendLine($"<StackPanel class=\"card\" gap-4 w-{i % 40} {{isOpen}}:hidden>");
			builder.AppendLine($"\t<TextBlock Text=\"Item {i}\" />");
			builder.AppendLine("\t<Button OnClick={count++} class=\"primary\" w-30>");
			builder.AppendLine("\t\tIncrement");
			builder.AppendLine("\t</Button>");
			builder.AppendLine("</StackPanel>");
			builder.AppendLine();
		}

		return builder.ToString();
	}
}

internal sealed class QuickScanLexerFactory
{
	private QuickScanLexerFactory(
		Func<string, object> createSourceText,
		Func<object, bool, Lexer> createLexer)
	{
		CreateSourceText = createSourceText;
		CreateLexer = createLexer;
	}

	public Func<string, object> CreateSourceText { get; }

	public Func<object, bool, Lexer> CreateLexer { get; }

	public static QuickScanLexerFactory Create()
	{
		var constructor = typeof(Lexer)
			.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.SingleOrDefault(static constructor =>
			{
				var parameters = constructor.GetParameters();
				return parameters.Length == 2 &&
					parameters[0].ParameterType.FullName == "Microsoft.CodeAnalysis.Text.SourceText" &&
					parameters[1].ParameterType == typeof(bool);
			});

		if (constructor is null)
		{
			throw new InvalidOperationException(
				"Lexer quick-scan benchmark requires Akbura.Generator to be built with ENABLE_QUICK_SCAN_BENCHMARK.");
		}

		var sourceTextType = constructor.GetParameters()[0].ParameterType;

		return new QuickScanLexerFactory(
			BuildSourceTextFactory(sourceTextType),
			BuildLexerFactory(constructor, sourceTextType));
	}

	private static Func<string, object> BuildSourceTextFactory(Type sourceTextType)
	{
		var fromMethod = sourceTextType
			.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Where(static method => method.Name == "From")
			.Where(static method =>
			{
				var parameters = method.GetParameters();
				return parameters.Length > 0 &&
					parameters[0].ParameterType == typeof(string) &&
					parameters.Skip(1).All(static parameter => parameter.HasDefaultValue);
			})
			.OrderBy(static method => method.GetParameters().Length)
			.FirstOrDefault();

		if (fromMethod is null)
		{
			throw new InvalidOperationException("Could not find a compatible SourceText.From(string, ...) method.");
		}

		var text = Expression.Parameter(typeof(string), "text");
		var arguments = new Expression[fromMethod.GetParameters().Length];
		arguments[0] = text;

		var parameters = fromMethod.GetParameters();
		for (var i = 1; i < parameters.Length; i++)
		{
			arguments[i] = CreateDefaultArgument(parameters[i]);
		}

		var call = Expression.Call(fromMethod, arguments);
		return Expression
			.Lambda<Func<string, object>>(Expression.Convert(call, typeof(object)), text)
			.Compile();
	}

	private static Func<object, bool, Lexer> BuildLexerFactory(ConstructorInfo constructor, Type sourceTextType)
	{
		var sourceText = Expression.Parameter(typeof(object), "sourceText");
		var enableQuickScanner = Expression.Parameter(typeof(bool), "enableQuickScanner");
		var newLexer = Expression.New(
			constructor,
			Expression.Convert(sourceText, sourceTextType),
			enableQuickScanner);

		return Expression
			.Lambda<Func<object, bool, Lexer>>(newLexer, sourceText, enableQuickScanner)
			.Compile();
	}

	private static Expression CreateDefaultArgument(ParameterInfo parameter)
	{
		if (parameter.DefaultValue is not null and not DBNull)
		{
			return Expression.Constant(parameter.DefaultValue, parameter.ParameterType);
		}

		return Expression.Default(parameter.ParameterType);
	}
}

public sealed class QuickScanBenchmarkConfig : ManualConfig
{
	public QuickScanBenchmarkConfig()
	{
		AddJob(
			Job.ShortRun
				.WithMsBuildArguments(
					"/p:EnableQuickScanBenchmark=true",
					"/p:EnableAkburaStats=true",
					"/p:DefineConstants=ENABLE_QUICK_SCAN_BENCHMARK")
				.DontEnforcePowerPlan()
				.WithId("ShortRun"));
    }
}
#endif
