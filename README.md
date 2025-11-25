# Akbura

Akbura is a UI framework for building applications using a declarative, component-based language.

The project is currently in **early experimental development**, and many parts of the system exist only as concepts. Expect frequent breaking changes during the initial stages as the language, code generation model, and runtime evolve.

If you want to follow progress or contribute ideas, you are welcome to join the community on **Discord** or **Telegram**. All drafts, specifications, and current design notes can be found in the `drafts-concepts` folder.

Akbura is an active exploration of what a modern reactive UI language for .NET could look like — and your feedback is invaluable.

## Getting Started

Akbura is currently in active development. When the framework becomes production‑ready, creating a new Akbura application will be simple and familiar for any .NET developer.

---

### 1. Install the Package

```bash
dotnet add package Akbura
```

This will add the core Akbura runtime and code generator to your project.

---

### 2. Create Your First Component

Create a file named `Counter.akbura`:

```akbura
// Counter.akbura

state int count = 0;

<Stack w-full h-full items-center>
	<Text FontSize="24">Count: {count}</Text>
	<Button Click={count++}>Increment</Button>
</Stack>
```

This defines a reactive component with one state variable and two UI elements.

---

### 3. Run the App

In your application's entry point (`Program.cs`):

```csharp
using Akbura;

AkburaRoot.Run<Counter>();
```

This will:

* Initialize the Akbura runtime
* Instantiate your component
* Create its generated view
* Mount it as the root UI element

## Getting Started with Avalonia

To mount Akbura components inside an existing Avalonia application, you can use `AkburaAvaloniaHost`.

---

### 1. Install the Package

```bash
dotnet add package Akbura
```

This installs the core Akbura runtime and compiler.

---

### 2. Create Your First Component

Create a file named `Counter.akbura`:

```akbura
// Counter.akbura

state int count = 0;

<Stack w-full h-full items-center>
	<Text FontSize="24">Count: {count}</Text>
	<Button Click={count++}>Increment</Button>
</Stack>
```

---

### 3. Use `AkburaAvaloniaHost` in your Avalonia App

In your Avalonia XAML file:

```xaml
<Window xmlns="https://github.com/avaloniaui"
		xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
		xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
		xmlns:akbura="clr-namespace:Akbura.Avalonia;assembly=Akbura.Avalonia"
		mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="450"
		x:Class="AvaloniaApplication.MainWindow"
		Title="AvaloniaApplication">

	<akbura:AkburaAvaloniaHost Component="{x:Type Counter}" />
</Window>
```

This will:

* Load the compiled `Counter` component
* Create its generated Avalonia view
* Mount it inside the window automatically

---

You can mount any Akbura component this way and freely mix Akbura UI with native Avalonia XAML.


## States

Akbura components can declare reactive state variables using the `state` keyword.

```akbura
// ExampleStates.akbura

state a = 0; // default value 0, implicit type
state MyDto b = new MyDto() { field1: "default", field2: 42 }; // explicit type + default value

<Grid Rows="*, *">
	
	<Button Click={a++} row-0>
		a = {a}
	</Buttom>

	<Stack row-1>
		<Input bind:Value={b.field1}/>
		<Input bind:Value={b.field2}/>
	</Stack>

</Grid>
```

Akbura compiler generates strongly-typed C# code:
```csharp
[StaticMount]
[AkburaComponent(AssemblyName="MyAssembly", Source = "ExampleStates.akbura")]
public partial class ExampleStates: AkburaComponent
{
	[State("a")]
	private int __a = 0;

	[State("b")]
	private MyDto __b = new MyDto { field1 = "default", field2 = 42 };

	private __ExampleStates__View_0_? _view0;

	protected override IBindedView Update()
	{
		return _view0 ??= new __ExampleStates__View_0_(this);
	}

	class __ExampleStates__View_0_: AvaloniaBindedView<ExampleStates>
	{
		[Component(Position = 166, Width = 189)]
		private Grid _grid0;

		[Component(...)]
		private Button _button0;

		[Component(...)]]
		private StackPanel _stackPanel0;

		[Component(...)]]
		private TextBox _input0;

		[Component(...)]]
		private TextBox _input1;

		public __ExampleStates__View_0_(ExampleStates owner) : base(owner)
		{
			_grid0 = new Grid();
			//....
			_button0 = new Button();
			//....
			_stackPanel = new StackPanel();
			//....
			_input0 = new TextBox();
			//....
			_input1 = new TextBox();
			//....
		}

		protected override Update()
		{
			var a = Owner.__a;
			var b = Owner.__b;

			_button0.Content = $"a = {a}";

			_input0 = b.field1;
			_input1 = b.field2;
		}
	}
}
```

### Binding to viewmodel

Akbura supports param `bind/out/in` semantics for connecting component state to external viewmodels.

```csharp
state MyViewModel vm = new MyViewModel();

state string name = bind vm.Name; // two-way binding to vm.Name property
state string fullName = out vm.FullName; // one-way binding from vm.FullName property, state now is readonly
state string surname = in vm.Surname; // one-way binding to vm.Surname property

<Stack gap-2>
	<Input bind:Value={name}    Placeholder="Name"/>
	<Input bind:Value={surname} Placeholder="Surname"/>
	<Input Value="Full Name: {fullName}"/>
</Stack>
```

## Parameters

Parameters allow a component to receive data from its parent. They behave similarly to constructor arguments, but are fully declarative and compile into strongly-typed C# fields.

A parameter:

* Can have a **default value**, making it optional.
* Can be **required** if no default value is provided.
* Can be used anywhere inside the component (markup or expressions).

### Example

```akbura
// MyCoolComponent.akbura

param string Title = "Default Title"; // default value
param int Count; // no default value, required

<Stack gap-2>
	<Text FontSize="20" FontWeight="Bold">{Title}</Text>
	<Text>Count: {Count}</Text>
</Stack>

// RootComponent.akbura

<MyCoolComponent Title="Hello World" Count={5}/>
```


## Parameters Binding

Parameters support directional data flow. Unlike parameters in some frameworks, Akbura parameters explicitly describe how values move between **parent => component** and **component => parent**.

### Parameter modes

| Declaration      | Direction          | Description                                                     |
| ---------------- | ------------------ | --------------------------------------------------------------- |
| `param T x`      | Parent => Component | **Default**. Parent sets value. Component cannot modify parent. |
| `param bind T x` | Parent <=> Component | Two-way binding. Changes sync both ways.                        |
| `param out T x`  | Component => Parent | Readonly for parent. Component pushes changes upward.           |

 **`param in T x` does not exist** — the default `param T x` already behaves like `in`.

---

## Example

```akbura
// BindableParam.akbura

param bind string Text = "Default"; // two-way binding
param out int Data = 0;              // component => parent only

<Stack gap-2>
	<Input bind:Value={Text}/>
	<Input bind:Value={Data}/>
</Stack>

// Root.akbura

state text = "";
state data = 0;

<BindableParam bind:Text={text} Data={42}/>        // ERROR: Data is readonly
<BindableParam bind:Text={text} bind:Data={42}/>    // ERROR: Data is readonly
<BindableParam bind:Text={text} out:Data={data}/>   // OK
```

### Explanation

* `Text` supports two-way binding => parent must also use `bind:`.
* `Data` is declared `out`, so parent **cannot** assign directly.
* Parent must specify `out:Data={...}` to receive updates.

---

## Modify Bindings

Akbura allows using `bind(...)` expression form for fine-grained control.
This is useful when you need custom logic for reading or writing.

✔ Only inside `bind(...)` you may use `@in` and `@out`.
These *do not* exist in parameter declarations.

```akbura
// Root.akbura

state text = "";
state data = 0;

<BindableParam Text={bind(
	@in: () => {
		Console.WriteLine("Writing to param Text");
		return text;
	},
	@out: value => {
		Console.WriteLine("Reading from BindableParam " + value);
		text = value;
	}
)}

out:Data={value => {
	data = value * 2;
	Console.WriteLine("Reading from readonly param BindableParam.Data");
}}/>
```


This fully reflects the correct behavior of parameter binding in Akbura.

## useEffect Declaration

Akbura provides an effect system similar to React’s `useEffect`. Effects run automatically in response to **state changes**, and can optionally be asynchronous with full cancellation support.

Effects allow you to:

* React to component lifecycle events
* Run logic when specific state values change
* Perform async work tied to component updates
* Handle cancellation and finalization

---

## Basic useEffect

```akbura
// UseEffectExample.akbura

state int count = 0;
state string message = "Hello";

useEffect() {
	Console.WriteLine("Component did mount");
}

// Reacts only to changes of `count`
useEffect(count) {
	Console.WriteLine($"Count changed to: {count}");
	Console.WriteLine($"Old value is: {@old(count)}");
}

// Reacts only to `message` changes
useEffect(message) {
	Console.WriteLine($"Message changed to: {message}");
	Console.WriteLine($"Old value is: {@old(message)}");
}

<Stack>
	<Input bind:Value={count}/>
	<Input bind:Value={message}/>
</Stack>
```


## Async useEffect

Effects can be asynchronous. Akbura automatically provides cancellation tokens and manages effect lifetimes.

```akbura
state int? data = null;

useEffect() {
	await Task.Delay(2000); // async work
	data = 42;
}
cancel {
	Console.WriteLine("Effect was cancelled");
}
finally {
	Console.WriteLine("Effect has completed");
}

if(data == null) {
	<Text>Data is loading...</Text>
}

<Text>Data is fetched! Data is {data}</Text>
```

## Cancellation Token Example

Akbura exposes the cancellation token via the special dependency `@cancel`.

```akbura
state int? data = null;
state bool canceled = false;
state string? query = 0;

useEffect(query, @cancel) {
	await Task.Delay(2000, @cancel);

	if(@cancel.IsCancellationRequested) {
		canceled = true;
		return;
	}

	data = 42;
}
cancel {
	canceled = true;
	Console.WriteLine("Effect was cancelled");
}
finally {
	Console.WriteLine("Effect has completed");
}

<Stack>
	<Input bind:Value={query} Placeholder="Enter query"/>
	<Text {!canceled}:hidden>Previous operation was canceled.</Text>
	<Text {data == null}:hidden>Fetched data is {data}</Text>
</Stack>
```

## Dependency Injection

Akbura supports dependency injection (DI) to provide services and shared resources directly into components. Any component can declare injected dependencies, which are automatically resolved from the configured DI container.

## Registering an `IServiceProvider`

### Using `AkburaRoot`

```csharp
AkburaRoot.Builder()
	.UseServiceProvider(yourServiceProvider)
	.Run<MyRoot>();
```

This configures the DI provider for all Akbura components in the application.

### Using Avalonia Host

```xaml
<akbura:AkburaAvaloniaHost
	Component="{x:Type MyRoot}"
	ServiceProvider="{... your service provider ...}" />
```

Here DI is bound per-host, allowing Akbura to interoperate with Avalonia and external DI systems.

### Injecting Dependencies into Components

To consume a service inside an Akbura component, use the `inject` keyword:

```akbura
inject ILogger<MyComponent> logger;

useEffect() {
	logger.LogInformation("MyComponent mounted");
}

<Text>
	Hello world!
	ILogger hashcode: {logger.GetHashCode()}
</Text>
```

## Commands

Commands in Akbura behave similarly to UI events, but with additional capabilities:

* **CanExecute semantics**
* **IsExecuting state tracking**
* **Async method support**
* **Typed arguments and return values**
* **Direct binding to C# commands**

They allow components to expose rich interaction points that integrate with both state and asynchronous workflows.


### Declaring a Command

Inside a component:

```akbura
// CustomButton.akbura

command int CustomClick(int a);

state int clicked = 0;

useEffect(CustomClick.IsExecuting) {
	Console.WriteLine("Command is executing");
}

<Block p-4 {CustomClick.IsExecuting}:disabled>
	<Button Click={() => {
		var result = await CustomClick.Invoke(clicked++);
		Console.WriteLine($"Result is {result}");
	}}/>
</Block>
```

### Generated Behavior

Every command automatically provides:

* **`.Invoke(args)`** — executes sync or async code
* **`.IsExecuting`** — a reactive boolean state
* 

### Using Commands from Another Component

```akbura
// Root.akbura

inject RootVm Vm;

<Stack>
	<CustomButton Click={() => Console.WriteLine("Hello world")} />
	<!-- No argument → argument ignored, default return value, no async -->

	<CustomButton Click={x => Console.WriteLine($"Hello world with {x}")} />
	<!-- Receives argument, no return value, sync execution -->

	<CustomButton Click={x => x * 2} />
	<!-- Returns explicit value, still sync -->

	<CustomButton Click={x => await Vm.Fetch(x)} />
	<!-- Async execution, awaited result passed to command -->

	<CustomButton Click={Vm.MyCommand} />
	<!-- Direct reference to a C# ICommand-like method or handler -->
</Stack>
```

## AKCSS

AKCSS is a CSS-inspired styling language designed for Akbura components.
It provides a familiar declarative syntax while integrating tightly with component state, pseudo-classes, and the Akbura rendering model.

AKCSS files allow you to define reusable style rules that are compiled into strongly typed C# classes.

---

## Basic Example

```akcss
.myclass {
	Background: "Red";

	@if(IsHovered) {
		Background: "Blue";
	}

	@if(this == Button) {
		Padding: 10;
	}

	@if(this is Button) {
		Padding: 5;
	}
}
```

Applied to a component:

```akbura
<Button class="myclass" />
```

---

## Generated C# Code

When compiled, Akcss generates an internal class representing the style logic:

```csharp
[AkcssClass("myclass", Source="...")]
[Observes("IsHovered")]
class __myclass_Akcss_ : AkcssStyleClass
{
	public override void ApplyOrUpdateStyle(AkburaControl control)
	{
		control.Background = "Red";

		if (control.IsHovered)
		{
			control.Background = "Blue";
		}

		if (control.UnderlyingControl.GetType() == typeof(Button))
		{
			control.Padding = 10;
		}

		if (control.UnderlyingControl is Button)
		{
			control.Padding = 5;
		}
	}
}
```

## Akcss Utilities

Akcss supports a utility system inspired by Tailwind, where concise class-like patterns expand into full property assignments. Utilities are defined inside an `@utilities` block.

```akcss
@utilities {
    .rounded {
        CornerRadius: 4;
    }

    .w-(double width) {
        Width: width * MyNamespace.MyStaticClass.Spacing;
    }

    .space-(int x)-(int y) {
        MarginLeft: x * MyNamespace.MyStaticClass.Spacing;
        MarginTop:  y * MyNamespace.MyStaticClass.Spacing;

        @if(x > y) {
            BorderThickness: x - y;
        }
    }
}
```

Using the utilities:

```akbura
<Block w-30 space-2-4 rounded />
```

## Akcss Utilities — Condition Prefix

Akcss utilities support **conditional prefixes**, allowing styles to be applied only when a specific condition or pseudo‑state is true.

This enables dynamic utility behavior without writing full AKCSS blocks.

```akcss
state bool isMobile = false;

<Box w-30 {isMobile}:w-20>
```

Here:

* `w-30` applies normally.
* `{isMobile}:w-20` applies *only when `isMobile` is true*.

Conditional prefixes may also use:

* component boolean states
* hover, checked and other pseudo-classes
* parameter values

Akcss resolves utilities in the following order:

* If multiple utilities share the same base name, **only the last matching one is applied**.
* Utilities have **higher priority** than regular AKCSS class styles.
* Conditional utilities override unconditional ones if their condition evaluates to true.
