// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.ClearScript.Util;

namespace Microsoft.ClearScript
{
    /// <summary>
    /// Provides optional script-callable utility functions.
    /// </summary>
    /// <remarks>
    /// Use <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c> to expose a
    /// <c>HostFunctions</c> instance to script code. Each instance can only be exposed in one
    /// script engine.
    /// </remarks>
    public class HostFunctions : IScriptableObject
    {
        private ScriptEngine engine;

        // ReSharper disable EmptyConstructor

        /// <summary>
        /// Initializes a new <c><see cref="HostFunctions"/></c> instance.
        /// </summary>
        public HostFunctions()
        {
            // the help file builder (SHFB) insists on an empty constructor here
        }

        // ReSharper restore EmptyConstructor

        #region script-callable interface

        // ReSharper disable InconsistentNaming

        // ReSharper disable GrammarMistakeInComment

        /// <summary>
        /// Creates an empty host object.
        /// </summary>
        /// <returns>A new empty host object.</returns>
        /// <remarks>
        /// This function is provided for script languages that do not support external
        /// instantiation. It creates an object that supports dynamic property addition and
        /// removal. The host can manipulate it via the <c><see cref="IPropertyBag"/></c> interface.
        /// </remarks>
        /// <example>
        /// The following code creates an empty host object and adds several properties to it.
        /// It assumes that an instance of <c><see cref="HostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// var item = host.newObj();
        /// item.label = "Widget";
        /// item.weight = 123.45;
        /// </code>
        /// </example>
        public PropertyBag newObj()
        {
            return new PropertyBag();
        }

        // ReSharper restore GrammarMistakeInComment

        /// <summary>
        /// Creates a host object of the specified type. This version is invoked if the specified
        /// type can be used as a type argument.
        /// </summary>
        /// <typeparam name="T">The type of object to create.</typeparam>
        /// <param name="args">Optional constructor arguments.</param>
        /// <returns>A new host object of the specified type.</returns>
        /// <remarks>
        /// <para>
        /// This function is provided for script languages that do not support external
        /// instantiation. It is overloaded with <c><see cref="newObj(object, object[])"/></c> and
        /// selected at runtime if <typeparamref name="T"/> can be used as a type argument.
        /// </para>
        /// <para>
        /// For information about the mapping between host members and script-callable properties
        /// and methods, see
        /// <c><see cref="ScriptEngine.AddHostObject(string, HostItemFlags, object)">AddHostObject</see></c>.
        /// </para>
        /// </remarks>
        /// <example>
        /// The following code imports the <c><see cref="System.Random"/></c> class, creates an
        /// instance using the
        /// <c><see href="https://docs.microsoft.com/en-us/dotnet/api/system.random.-ctor#System_Random__ctor_System_Int32_">Random(Int32)</see></c>
        /// constructor, and calls the <c><see cref="System.Random.NextDouble"/></c> method.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// var RandomT = host.type("System.Random");
        /// var random = host.newObj(RandomT, 100);
        /// var value = random.NextDouble();
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public T newObj<T>(params object[] args)
        {
            return (T)typeof(T).CreateInstance(args);
        }

        /// <summary>
        /// Creates a host object of the specified type. This version is invoked if the specified
        /// type cannot be used as a type argument.
        /// </summary>
        /// <param name="type">The type of object to create.</param>
        /// <param name="args">Optional constructor arguments.</param>
        /// <returns>A new host object of the specified type.</returns>
        /// <remarks>
        /// <para>
        /// This function is provided for script languages that do not support external
        /// instantiation. It is overloaded with <c><see cref="newObj{T}"/></c> and selected at runtime if
        /// <paramref name="type"/> cannot be used as a type argument. Note that this applies
        /// to some host types that support instantiation, such as certain COM/ActiveX types.
        /// </para>
        /// <para>
        /// For information about the mapping between host members and script-callable properties
        /// and methods, see
        /// <c><see cref="ScriptEngine.AddHostObject(string, HostItemFlags, object)">AddHostObject</see></c>.
        /// </para>
        /// </remarks>
        public object newObj(object type, params object[] args)
        {
            return GetUniqueHostType(type, "type").CreateInstance(args);
        }

        /// <summary>
        /// Performs dynamic instantiation.
        /// </summary>
        /// <param name="target">The dynamic host object that provides the instantiation operation to perform.</param>
        /// <param name="args">Optional instantiation arguments.</param>
        /// <returns>The result of the operation, which is usually a new dynamic host object.</returns>
        /// <remarks>
        /// This function is provided for script languages that do not support external
        /// instantiation.
        /// </remarks>
        public object newObj(IDynamicMetaObjectProvider target, params object[] args)
        {
            MiscHelpers.VerifyNonNullArgument(target, nameof(target));

            if (target.GetMetaObject(Expression.Constant(target)).TryCreateInstance(args, out var result))
            {
                return result;
            }

            throw new InvalidOperationException("Invalid dynamic instantiation");
        }

        /// <summary>
        /// Creates a host array with the specified element type.
        /// </summary>
        /// <typeparam name="T">The element type of the array to create.</typeparam>
        /// <param name="lengths">One or more integers representing the array dimension lengths.</param>
        /// <returns>A new host array with the specified element type.</returns>
        /// <remarks>
        /// For information about the mapping between host members and script-callable properties
        /// and methods, see
        /// <c><see cref="ScriptEngine.AddHostObject(string, HostItemFlags, object)">AddHostObject</see></c>.
        /// </remarks>
        /// <example>
        /// The following code creates a 5x3 host array of strings.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// var StringT = host.type("System.String");
        /// var array = host.newArr(StringT, 5, 3);
        /// </code>
        /// </example>
        /// <c><seealso cref="HostFunctions.newArr(int[])"/></c>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object newArr<T>(params int[] lengths)
        {
            return Array.CreateInstance(typeof(T), lengths);
        }

        /// <summary>
        /// Creates a host array with <c><see cref="System.Object"/></c> as the element type.
        /// </summary>
        /// <param name="lengths">One or more integers representing the array dimension lengths.</param>
        /// <returns>A new host array with <c><see cref="System.Object"/></c> as the element type.</returns>
        /// <remarks>
        /// For information about the mapping between host members and script-callable properties
        /// and methods, see
        /// <c><see cref="ScriptEngine.AddHostObject(string, HostItemFlags, object)">AddHostObject</see></c>.
        /// </remarks>
        /// <c><seealso cref="HostFunctions.newArr{T}(int[])"/></c>
        public object newArr(params int[] lengths)
        {
            return newArr<object>(lengths);
        }

        // ReSharper disable GrammarMistakeInComment

        /// <summary>
        /// Creates a host variable of the specified type.
        /// </summary>
        /// <typeparam name="T">The type of variable to create.</typeparam>
        /// <param name="initValue">An optional initial value for the variable.</param>
        /// <returns>A new host variable of the specified type.</returns>
        /// <remarks>
        /// <para>
        /// A host variable is a strongly typed object that holds a value of the specified type.
        /// Host variables are useful for passing method arguments by reference. In addition to
        /// being generally interchangeable with their stored values, host variables support the
        /// following properties:
        /// </para>
        /// <para>
        /// <list type="table">
        ///     <listheader>
        ///         <term>Property</term>
        ///         <term>Access</term>
        ///         <description>Description</description>
        ///     </listheader>
        ///     <item>
        ///         <term><c>value</c></term>
        ///         <term>read-write</term>
        ///         <description>The current value of the host variable.</description>
        ///     </item>
        ///     <item>
        ///         <term><c>out</c></term>
        ///         <term>read-only</term>
        ///         <description>A reference to the host variable that can be passed as an <c><see href="https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/out-parameter-modifier">out</see></c> argument.</description>
        ///     </item>
        ///     <item>
        ///         <term><c>ref</c></term>
        ///         <term>read-only</term>
        ///         <description>A reference to the host variable that can be passed as a <c><see href="https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/ref">ref</see></c> argument.</description>
        ///     </item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <example>
        /// The following code demonstrates using a host variable to invoke a method with an
        /// <c>out</c> parameter.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import a dictionary type
        /// var StringT = host.type("System.String");
        /// var StringDictT = host.type("System.Collections.Generic.Dictionary", StringT, StringT);
        /// // create and populate a dictionary
        /// var dict = host.newObj(StringDictT);
        /// dict.Add("foo", "bar");
        /// dict.Add("baz", "qux");
        /// // look up a dictionary entry
        /// var result = host.newVar(StringT);
        /// var found = dict.TryGetValue("baz", result.out);
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object newVar<T>(T initValue = default)
        {
            return new HostVariable<T>(initValue);
        }

        // ReSharper restore GrammarMistakeInComment

        /// <summary>
        /// Creates a delegate that invokes a script function.
        /// </summary>
        /// <typeparam name="T">The type of delegate to create.</typeparam>
        /// <param name="scriptFunc">The script function for which to create a delegate.</param>
        /// <returns>A new delegate that invokes the specified script function.</returns>
        /// <remarks>
        /// If the delegate signature includes parameters passed by reference, the corresponding
        /// arguments to the script function will be <see cref="newVar{T}">host variables</see>.
        /// The script function can set the value of an output argument by assigning the
        /// corresponding host variable's <c>value</c> property.
        /// </remarks>
        /// <example>
        /// The following code demonstrates delegating a callback to a script function.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // create and populate an array of integers
        /// var EnumerableT = host.type("System.Linq.Enumerable", "System.Core");
        /// var array = EnumerableT.Range(1, 5).ToArray();
        /// // import the callback type required to call Array.ForEach
        /// var Int32T = host.type("System.Int32");
        /// var CallbackT = host.type("System.Action", Int32T);
        /// // use Array.ForEach to calculate a sum
        /// var sum = 0;
        /// var ArrayT = host.type("System.Array");
        /// ArrayT.ForEach(array, host.del(CallbackT, function (value) { sum += value; }));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, string, object[])"/></c>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public T del<T>(object scriptFunc)
        {
            return DelegateFactory.CreateDelegate<T>(GetEngine(), scriptFunc);
        }

        /// <summary>
        /// Creates a delegate that invokes a script function and returns no value.
        /// </summary>
        /// <param name="argCount">The number of arguments to pass to the script function.</param>
        /// <param name="scriptFunc">The script function for which to create a delegate.</param>
        /// <returns>A new delegate that invokes the specified script function and returns no value.</returns>
        /// <remarks>
        /// This function creates a delegate that accepts <paramref name="argCount"/> arguments and
        /// returns no value. The type of all parameters is <c><see cref="System.Object"/></c>. Such a
        /// delegate is often useful in strongly typed contexts because of
        /// <see href="https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/covariance-contravariance/">contravariance</see>.
        /// </remarks>
        /// <example>
        /// The following code demonstrates delegating a callback to a script function.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // create and populate an array of strings
        /// var StringT = host.type("System.String");
        /// var array = host.newArr(StringT, 3);
        /// array.SetValue("first", 0);
        /// array.SetValue("second", 1);
        /// array.SetValue("third", 2);
        /// // use Array.ForEach to generate console output
        /// var ArrayT = host.type("System.Array");
        /// var ConsoleT = host.type("System.Console");
        /// ArrayT.ForEach(array, host.proc(1, function (value) { ConsoleT.WriteLine(value); }));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        /// <c><seealso cref="newArr{T}"/></c>
        public object proc(int argCount, object scriptFunc)
        {
            return DelegateFactory.CreateProc(GetEngine(), scriptFunc, argCount);
        }

        /// <summary>
        /// Creates a delegate that invokes a script function and returns a value of the specified type.
        /// </summary>
        /// <typeparam name="T">The return value type.</typeparam>
        /// <param name="argCount">The number of arguments to pass to the script function.</param>
        /// <param name="scriptFunc">The script function for which to create a delegate.</param>
        /// <returns>A new delegate that invokes the specified script function and returns a value of the specified type.</returns>
        /// <remarks>
        /// This function creates a delegate that accepts <paramref name="argCount"/> arguments and
        /// returns a value of the specified type. The type of all parameters is
        /// <c><see cref="System.Object"/></c>. Such a delegate is often useful in strongly typed contexts
        /// because of
        /// <see href="https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/covariance-contravariance/">contravariance</see>.
        /// </remarks>
        /// <example>
        /// The following code demonstrates delegating a callback to a script function.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // create and populate an array of strings
        /// var StringT = host.type("System.String");
        /// var array = host.newArr(StringT, 3);
        /// array.SetValue("first", 0);
        /// array.SetValue("second", 1);
        /// array.SetValue("third", 2);
        /// // import LINQ extensions
        /// var EnumerableT = host.type("System.Linq.Enumerable", "System.Core");
        /// // use LINQ to create an array of modified strings
        /// var selector = host.func(StringT, 1, function (value) { return value.toUpperCase(); });
        /// array = array.Select(selector).ToArray();
        /// </code>
        /// </example>
        /// <c><seealso cref="HostFunctions.func(int, object)"/></c>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, string, object[])"/></c>
        public object func<T>(int argCount, object scriptFunc)
        {
            return DelegateFactory.CreateFunc<T>(GetEngine(), scriptFunc, argCount);
        }

        /// <summary>
        /// Creates a delegate that invokes a script function and returns its result value.
        /// </summary>
        /// <param name="argCount">The number of arguments to pass to the script function.</param>
        /// <param name="scriptFunc">The script function for which to create a delegate.</param>
        /// <returns>A new delegate that invokes the specified script function and returns its result value.</returns>
        /// <remarks>
        /// <para>
        /// This function creates a delegate that accepts <paramref name="argCount"/> arguments and
        /// returns the result of invoking <paramref name="scriptFunc"/>. The type of all
        /// parameters and the return value is <c><see cref="System.Object"/></c>. Such a delegate is
        /// often useful in strongly typed contexts because of
        /// <see href="https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/covariance-contravariance/">contravariance</see>.
        /// </para>
        /// <para>
        /// For information about the types of result values that script code can return, see
        /// <c><see cref="ScriptEngine.Evaluate(string, bool, string)"/></c>.
        /// </para>
        /// </remarks>
        /// <c><seealso cref="HostFunctions.func{T}(int, object)"/></c>
        public object func(int argCount, object scriptFunc)
        {
            return func<object>(argCount, scriptFunc);
        }

        /// <summary>
        /// Gets the <c><see cref="System.Type"/></c> for the specified host type. This version is invoked
        /// if the specified object can be used as a type argument.
        /// </summary>
        /// <typeparam name="T">The host type for which to get the <c><see cref="System.Type"/></c>.</typeparam>
        /// <returns>The <c><see cref="System.Type"/></c> for the specified host type.</returns>
        /// <remarks>
        /// <para>
        /// This function is similar to C#'s
        /// <c><see href="https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/typeof">typeof</see></c>
        /// operator. It is overloaded with <c><see cref="typeOf(object)"/></c> and selected at runtime if
        /// <typeparamref name="T"/> can be used as a type argument.
        /// </para>
        /// <para>
        /// This function throws an exception if the script engine's
        /// <c><see cref="ScriptEngine.AllowReflection"/></c> property is set to <c>false</c>.
        /// </para>
        /// </remarks>
        /// <example>
        /// The following code retrieves the assembly-qualified name of a host type.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// var StringT = host.type("System.String");
        /// var name = host.typeOf(StringT).AssemblyQualifiedName;
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public Type typeOf<T>()
        {
            GetEngine().CheckReflection();
            return typeof(T);
        }

        /// <summary>
        /// Gets the <c><see cref="System.Type"/></c> for the specified host type. This version is invoked
        /// if the specified object cannot be used as a type argument.
        /// </summary>
        /// <param name="value">The host type for which to get the <c><see cref="System.Type"/></c>.</param>
        /// <returns>The <c><see cref="System.Type"/></c> for the specified host type.</returns>
        /// <remarks>
        /// <para>
        /// This function is similar to C#'s
        /// <c><see href="https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/typeof">typeof</see></c>
        /// operator. It is overloaded with <c><see cref="typeOf{T}"/></c> and selected at runtime if
        /// <paramref name="value"/> cannot be used as a type argument. Note that this applies to
        /// some host types; examples are static types and overloaded generic type groups.
        /// </para>
        /// <para>
        /// This function throws an exception if the script engine's
        /// <c><see cref="ScriptEngine.AllowReflection"/></c> property is set to <c>false</c>.
        /// </para>
        /// </remarks>
        /// <example>
        /// The following code retrieves the assembly-qualified name of a host type.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// var ConsoleT = host.type("System.Console");
        /// var name = host.typeOf(ConsoleT).AssemblyQualifiedName;
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public Type typeOf(object value)
        {
            GetEngine().CheckReflection();
            return GetUniqueHostType(value, "value");
        }

        /// <summary>
        /// Determines whether an object is compatible with the specified host type.
        /// </summary>
        /// <typeparam name="T">The host type with which to test <paramref name="value"/> for compatibility.</typeparam>
        /// <param name="value">The object to test for compatibility with the specified host type.</param>
        /// <returns><c>True</c> if <paramref name="value"/> is compatible with the specified type, <c>false</c> otherwise.</returns>
        /// <remarks>
        /// This function is similar to C#'s
        /// <c><see href="https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/is">is</see></c>
        /// operator.
        /// </remarks>
        /// <example>
        /// The following code defines a function that determines whether an object implements
        /// <c><see cref="System.IComparable"/></c>.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// function isComparable(value)
        /// {
        ///     var IComparableT = host.type("System.IComparable");
        ///     return host.isType(IComparableT, value);
        /// }
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public bool isType<T>(object value)
        {
            return value is T;
        }

        /// <summary>
        /// Casts an object to the specified host type, returning <c>null</c> if the cast fails.
        /// </summary>
        /// <typeparam name="T">The host type to which to cast <paramref name="value"/>.</typeparam>
        /// <param name="value">The object to cast to the specified host type.</param>
        /// <returns>The result of the cast if successful, <c>null</c> otherwise.</returns>
        /// <remarks>
        /// This function is similar to C#'s
        /// <c><see href="https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/as">as</see></c>
        /// operator.
        /// </remarks>
        /// <example>
        /// The following code defines a function that disposes an object if it implements
        /// <c><see cref="System.IDisposable"/></c>.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// function dispose(value)
        /// {
        ///     var IDisposableT = host.type("System.IDisposable");
        ///     var disposable = host.asType(IDisposableT, value);
        ///     if (disposable) {
        ///         disposable.Dispose();
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object asType<T>(object value) where T : class
        {
            return HostItem.Wrap(GetEngine(), value as T, typeof(T));
        }

        /// <summary>
        /// Casts an object to the specified host type.
        /// </summary>
        /// <typeparam name="T">The host type to which to cast <paramref name="value"/>.</typeparam>
        /// <param name="value">The object to cast to the specified host type.</param>
        /// <returns>The result of the cast.</returns>
        /// <remarks>
        /// If the cast fails, this function throws an exception.
        /// </remarks>
        /// <example>
        /// The following code casts a floating-point value to a 32-bit integer.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// var Int32T = host.type("System.Int32");
        /// var intValue = host.cast(Int32T, 12.5);
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object cast<T>(object value)
        {
            return HostItem.Wrap(GetEngine(), value.DynamicCast<T>(), typeof(T));
        }

        /// <summary>
        /// Determines whether an object is a host type. This version is invoked if the specified
        /// object cannot be used as a type argument.
        /// </summary>
        /// <param name="value">The object to test.</param>
        /// <returns><c>True</c> if <paramref name="value"/> is a host type, <c>false</c> otherwise.</returns>
        /// <remarks>
        /// This function is overloaded with <c><see cref="isTypeObj{T}"/></c> and selected at runtime if
        /// <paramref name="value"/> cannot be used as a type argument. Note that this applies to
        /// some host types; examples are static types and overloaded generic type groups.
        /// </remarks>
        public bool isTypeObj(object value)
        {
            return value is HostType;
        }

        // ReSharper disable UnusedTypeParameter

        /// <summary>
        /// Determines whether an object is a host type. This version is invoked if the specified
        /// object can be used as a type argument.
        /// </summary>
        /// <typeparam name="T">The host type (ignored).</typeparam>
        /// <returns><c>True</c>.</returns>
        /// <remarks>
        /// This function is overloaded with <c><see cref="isTypeObj(object)"/></c> and selected at
        /// runtime if <typeparamref name="T"/> can be used as a type argument. Because type
        /// arguments are always host types, this method ignores its type argument and always
        /// returns <c>true</c>.
        /// </remarks>
        public bool isTypeObj<T>()
        {
            return true;
        }

        // ReSharper restore UnusedTypeParameter

        /// <summary>
        /// Determines whether the specified value is <c>null</c>.
        /// </summary>
        /// <param name="value">The value to test.</param>
        /// <returns><c>True</c> if <paramref name="value"/> is <c>null</c>, <c>false</c> otherwise.</returns>
        /// <remarks>
        /// Use this function to test field, property, and method return values when <c>null</c>
        /// result wrapping is in effect (see 
        /// <c><see cref="ScriptMemberFlags.WrapNullResult"/></c> and
        /// <c><see cref="ScriptEngine.EnableNullResultWrapping"/></c>).
        /// </remarks>
        /// <c><seealso cref="ScriptMemberFlags.WrapNullResult"/></c>
        /// <c><seealso cref="ScriptEngine.EnableNullResultWrapping"/></c>
        public bool isNull(object value)
        {
            return value is null;
        }

        /// <summary>
        /// Creates a strongly typed flag set.
        /// </summary>
        /// <typeparam name="T">The type of flag set to create.</typeparam>
        /// <param name="args">The flags to include in the flag set.</param>
        /// <returns>A strongly typed flag set containing the specified flags.</returns>
        /// <remarks>
        /// This function throws an exception if <typeparamref name="T"/> is not a flag set type.
        /// </remarks>
        /// <example>
        /// The following code demonstrates using a strongly typed flag set.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import URI types
        /// var UriT = host.type("System.Uri", "System");
        /// var UriFormatT = host.type("System.UriFormat", "System");
        /// var UriComponentsT = host.type("System.UriComponents", "System");
        /// // create a URI
        /// var uri = host.newObj(UriT, "http://www.example.com:8080/path/to/file/sample.htm?x=1&amp;y=2");
        /// // extract URI components
        /// var components = host.flags(UriComponentsT.Scheme, UriComponentsT.Host, UriComponentsT.Path);
        /// var result = uri.GetComponents(components, UriFormatT.Unescaped);
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, string, object[])"/></c>
        public T flags<T>(params T[] args)
        {
            var type = typeof(T);
            if (!type.IsFlagsEnum(ScriptEngine.Current))
            {
                throw new InvalidOperationException(MiscHelpers.FormatInvariant("{0} is not a flag set type", type.GetFullFriendlyName()));
            }

            try
            {
                return (T)Enum.ToObject(typeof(T), args.Aggregate(0UL, (flags, arg) => flags | Convert.ToUInt64(arg, CultureInfo.InvariantCulture)));
            }
            catch (OverflowException)
            {
                return (T)Enum.ToObject(typeof(T), args.Aggregate(0L, (flags, arg) => flags | Convert.ToInt64(arg, CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.SByte"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.SByte"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.SByte"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.SByte"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.SByte"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.SByte");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toSByte(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toSByte(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToSByte(value));
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.Byte"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.Byte"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.Byte"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.Byte"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.Byte"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.Byte");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toByte(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toByte(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToByte(value));
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.Int16"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.Int16"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.Int16"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.Int16"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.Int16"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.Int16");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toInt16(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toInt16(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToInt16(value));
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.UInt16"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.UInt16"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.UInt16"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.UInt16"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.UInt16"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.UInt16");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toUInt16(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toUInt16(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToUInt16(value));
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.Char"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.Char"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.Char"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.Char"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.Char"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.Char");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toChar(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toChar(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToChar(value));
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.Int32"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.Int32"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.Int32"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.Int32"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.Int32"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.Int32");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toInt32(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toInt32(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToInt32(value));
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.UInt32"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.UInt32"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.UInt32"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.UInt32"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.UInt32"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.UInt32");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toUInt32(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toUInt32(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToUInt32(value));
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.Int64"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.Int64"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.Int64"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.Int64"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.Int64"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.Int64");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toInt64(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toInt64(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToInt64(value));
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.UInt64"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.UInt64"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.UInt64"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.UInt64"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.UInt64"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.UInt64");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toUInt64(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toUInt64(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToUInt64(value));
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.Single"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.Single"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.Single"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.Single"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.Single"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.Single");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toSingle(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toSingle(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToSingle(value));
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.Double"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.Double"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.Double"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.Double"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.Double"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.Double");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toDouble(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toDouble(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToDouble(value));
        }

        /// <summary>
        /// Converts the specified value to a strongly typed <c><see cref="System.Decimal"/></c> instance.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>An object that can be passed to a parameter of type <c><see cref="System.Decimal"/></c>.</returns>
        /// <remarks>
        /// This function converts <paramref name="value"/> to <c><see cref="System.Decimal"/></c> and
        /// packages the result to retain its numeric type across the host-script boundary. It may
        /// be useful for passing arguments to <c><see cref="System.Decimal"/></c> parameters if the script
        /// engine does not support that type natively.
        /// </remarks>
        /// <example>
        /// The following code adds an element of type <c><see cref="System.Decimal"/></c> to a strongly
        /// typed list.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ElementT = host.type("System.Decimal");
        /// var ListT = host.type("System.Collections.Generic.List", ElementT);
        /// // create a list
        /// var list = host.newObj(ListT);
        /// // add a list element
        /// list.Add(host.toDecimal(42));
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public object toDecimal(IConvertible value)
        {
            return HostObject.Wrap(Convert.ToDecimal(value));
        }

        /// <summary>
        /// Gets the value of a property in a dynamic host object that implements <c><see cref="IPropertyBag"/></c>.
        /// </summary>
        /// <param name="target">The dynamic host object that contains the property to get.</param>
        /// <param name="name">The name of the property to get.</param>
        /// <returns>The value of the specified property.</returns>
        /// <remarks>
        /// This function is provided for script languages that do not support dynamic properties.
        /// </remarks>
        public object getProperty(IPropertyBag target, string name)
        {
            MiscHelpers.VerifyNonNullArgument(target, nameof(target));

            if (target.TryGetValue(name, out var result))
            {
                return result;
            }

            return Nonexistent.Value;
        }

        /// <summary>
        /// Sets a property value in a dynamic host object that implements <c><see cref="IPropertyBag"/></c>.
        /// </summary>
        /// <param name="target">The dynamic host object that contains the property to set.</param>
        /// <param name="name">The name of the property to set.</param>
        /// <param name="value">The new value of the specified property.</param>
        /// <returns>The result of the operation, which is usually the value assigned to the specified property.</returns>
        /// <remarks>
        /// This function is provided for script languages that do not support dynamic properties.
        /// </remarks>
        public object setProperty(IPropertyBag target, string name, object value)
        {
            MiscHelpers.VerifyNonNullArgument(target, nameof(target));
            return target[name] = value;
        }

        /// <summary>
        /// Removes a property from a dynamic host object that implements <c><see cref="IPropertyBag"/></c>.
        /// </summary>
        /// <param name="target">The dynamic host object that contains the property to remove.</param>
        /// <param name="name">The name of the property to remove.</param>
        /// <returns><c>True</c> if the property was found and removed, <c>false</c> otherwise.</returns>
        /// <remarks>
        /// This function is provided for script languages that do not support dynamic properties.
        /// </remarks>
        public bool removeProperty(IPropertyBag target, string name)
        {
            MiscHelpers.VerifyNonNullArgument(target, nameof(target));
            return target.Remove(name);
        }

        /// <summary>
        /// Gets the value of a property in a dynamic host object that implements <c><see cref="IDynamicMetaObjectProvider"/></c>.
        /// </summary>
        /// <param name="target">The dynamic host object that contains the property to get.</param>
        /// <param name="name">The name of the property to get.</param>
        /// <returns>The value of the specified property.</returns>
        /// <remarks>
        /// This function is provided for script languages that do not support dynamic properties.
        /// </remarks>
        public object getProperty(IDynamicMetaObjectProvider target, string name)
        {
            MiscHelpers.VerifyNonNullArgument(target, nameof(target));

            if (target.GetMetaObject(Expression.Constant(target)).TryGetMember(name, out var result))
            {
                return result;
            }

            return Nonexistent.Value;
        }

        /// <summary>
        /// Sets a property value in a dynamic host object that implements <c><see cref="IDynamicMetaObjectProvider"/></c>.
        /// </summary>
        /// <param name="target">The dynamic host object that contains the property to set.</param>
        /// <param name="name">The name of the property to set.</param>
        /// <param name="value">The new value of the specified property.</param>
        /// <returns>The result of the operation, which is usually the value assigned to the specified property.</returns>
        /// <remarks>
        /// This function is provided for script languages that do not support dynamic properties.
        /// </remarks>
        public object setProperty(IDynamicMetaObjectProvider target, string name, object value)
        {
            MiscHelpers.VerifyNonNullArgument(target, nameof(target));

            if (target.GetMetaObject(Expression.Constant(target)).TrySetMember(name, value, out var result))
            {
                return result;
            }

            throw new InvalidOperationException("Invalid dynamic property assignment");
        }

        /// <summary>
        /// Removes a property from a dynamic host object that implements <c><see cref="IDynamicMetaObjectProvider"/></c>.
        /// </summary>
        /// <param name="target">The dynamic host object that contains the property to remove.</param>
        /// <param name="name">The name of the property to remove.</param>
        /// <returns><c>True</c> if the property was found and removed, <c>false</c> otherwise.</returns>
        /// <remarks>
        /// This function is provided for script languages that do not support dynamic properties.
        /// </remarks>
        public bool removeProperty(IDynamicMetaObjectProvider target, string name)
        {
            MiscHelpers.VerifyNonNullArgument(target, nameof(target));

            if (target.GetMetaObject(Expression.Constant(target)).TryDeleteMember(name, out var result))
            {
                return result;
            }

            throw new InvalidOperationException("Invalid dynamic property deletion");
        }

        /// <summary>
        /// Gets the value of an element in a dynamic host object that implements <c><see cref="IDynamicMetaObjectProvider"/></c>.
        /// </summary>
        /// <param name="target">The dynamic host object that contains the element to get.</param>
        /// <param name="indices">One or more indices that identify the element to get.</param>
        /// <returns>The value of the specified element.</returns>
        /// <remarks>
        /// This function is provided for script languages that do not support general indexing.
        /// </remarks>
        public object getElement(IDynamicMetaObjectProvider target, params object[] indices)
        {
            MiscHelpers.VerifyNonNullArgument(target, nameof(target));

            if (target.GetMetaObject(Expression.Constant(target)).TryGetIndex(indices, out var result))
            {
                return result;
            }

            return Nonexistent.Value;

        }

        /// <summary>
        /// Sets an element value in a dynamic host object that implements <c><see cref="IDynamicMetaObjectProvider"/></c>.
        /// </summary>
        /// <param name="target">The dynamic host object that contains the element to set.</param>
        /// <param name="value">The new value of the element.</param>
        /// <param name="indices">One or more indices that identify the element to set.</param>
        /// <returns>The result of the operation, which is usually the value assigned to the specified element.</returns>
        /// <remarks>
        /// This function is provided for script languages that do not support general indexing.
        /// </remarks>
        public object setElement(IDynamicMetaObjectProvider target, object value, params object[] indices)
        {
            MiscHelpers.VerifyNonNullArgument(target, nameof(target));

            if (target.GetMetaObject(Expression.Constant(target)).TrySetIndex(indices, value, out var result))
            {
                return result;
            }

            throw new InvalidOperationException("Invalid dynamic element assignment");
        }

        /// <summary>
        /// Removes an element from a dynamic host object that implements <c><see cref="IDynamicMetaObjectProvider"/></c>.
        /// </summary>
        /// <param name="target">The dynamic host object that contains the element to remove.</param>
        /// <param name="indices">One or more indices that identify the element to remove.</param>
        /// <returns><c>True</c> if the element was found and removed, <c>false</c> otherwise.</returns>
        /// <remarks>
        /// This function is provided for script languages that do not support general indexing.
        /// </remarks>
        public bool removeElement(IDynamicMetaObjectProvider target, params object[] indices)
        {
            MiscHelpers.VerifyNonNullArgument(target, nameof(target));

            if (target.GetMetaObject(Expression.Constant(target)).TryDeleteIndex(indices, out var result))
            {
                return result;
            }

            throw new InvalidOperationException("Invalid dynamic element deletion");
        }

        /// <summary>
        /// Casts a dynamic host object to its static type.
        /// </summary>
        /// <param name="value">The object to cast to its static type.</param>
        /// <returns>The specified object in its static type form, stripped of its dynamic members.</returns>
        /// <remarks>
        /// A dynamic host object that implements <c><see cref="IDynamicMetaObjectProvider"/></c> may have
        /// dynamic members that override members of its static type. This function can be used to
        /// gain access to type members overridden in this manner.
        /// </remarks>
        public object toStaticType(IDynamicMetaObjectProvider value)
        {
            return HostItem.Wrap(GetEngine(), value, HostItemFlags.HideDynamicMembers);
        }

        /// <summary>
        /// Allows script code to handle host exceptions.
        /// </summary>
        /// <param name="tryFunc">A script function that invokes one or more host methods or properties.</param>
        /// <param name="catchFunc">A script function to invoke if <paramref name="tryFunc"/> throws an exception.</param>
        /// <param name="finallyFunc">An optional script function that performs cleanup for the operation.</param>
        /// <returns><c>True</c> if <paramref name="tryFunc"/> completed successfully, <c>false</c> if it threw an exception that was handled by <paramref name="catchFunc"/>.</returns>
        /// <remarks>
        /// This function uses a <c>try</c>-<c>catch</c>-<c>finally</c> statement to invoke
        /// <paramref name="tryFunc"/>. If an exception is thrown, it is caught and passed to
        /// <paramref name="catchFunc"/> for analysis. If <paramref name="catchFunc"/> returns
        /// <c>false</c>, the exception is rethrown. Regardless of the outcome,
        /// <paramref name="finallyFunc"/>, if specified, is invoked as a final step before the
        /// function exits.
        /// </remarks>
        /// <example>
        /// The following code demonstrates handling host exceptions in script code.
        /// It assumes that an instance of <c><see cref="ExtendedHostFunctions"/></c> is exposed under
        /// the name "host"
        /// (see <c><see cref="ScriptEngine.AddHostObject(string, object)">AddHostObject</see></c>).
        /// <code lang="JavaScript">
        /// // import types
        /// var ConsoleT = host.type("System.Console");
        /// var WebClientT = host.type("System.Net.WebClient", "System");
        /// // create a Web client
        /// var webClient = host.newObj(WebClientT);
        /// host.tryCatch(
        ///     function () {
        ///         // download Web document
        ///         ConsoleT.WriteLine(webClient.DownloadString("http://cnn.com"));
        ///     },
        ///     function (exception) {
        ///         // dump exception
        ///         ConsoleT.WriteLine("*** ERROR: " + exception.GetBaseException().ToString());
        ///         return true;
        ///     },
        ///     function () {
        ///         // clean up
        ///         ConsoleT.WriteLine("*** CLEANING UP ***");
        ///         webClient.Dispose();
        ///     }
        /// );
        /// </code>
        /// </example>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, string, object[])"/></c>
        /// <c><seealso cref="ExtendedHostFunctions.type(string, object[])"/></c>
        public bool tryCatch(object tryFunc, object catchFunc, object finallyFunc = null)
        {
            MiscHelpers.VerifyNonNullArgument(tryFunc, nameof(tryFunc));
            MiscHelpers.VerifyNonNullArgument(catchFunc, nameof(catchFunc));

            try
            {
                ((ScriptObject)tryFunc).InvokeAsFunction();
                return true;
            }
            catch (Exception exception)
            {
                if (!Convert.ToBoolean(((ScriptObject)catchFunc).InvokeAsFunction(exception)))
                {
                    throw;
                }

                return false;
            }
            finally
            {
                if (finallyFunc is not null)
                {
                    ((ScriptObject)finallyFunc).InvokeAsFunction();
                }
            }
        }

        // ReSharper restore InconsistentNaming

        #endregion

        internal ScriptEngine GetEngine()
        {
            var activeEngine = ScriptEngine.Current ?? engine;
            if (activeEngine is null)
            {
                throw new InvalidOperationException("Operation requires a script engine");
            }

            return activeEngine;
        }

        internal static Type GetUniqueHostType(object type, string paramName)
        {
            var hostType = type as HostType;
            if (hostType is null)
            {
                throw new ArgumentException("Invalid host type", paramName);
            }

            if (hostType.Types.Length > 1)
            {
                throw new ArgumentException(MiscHelpers.FormatInvariant("'{0}' does not identify a unique host type", hostType.Types[0].GetLocator()), paramName);
            }

            return hostType.Types[0];
        }

        #region IScriptableObject implementation

        // ReSharper disable ParameterHidesMember

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes", Justification = "This member is not expected to be re-implemented in derived classes.")]
        void IScriptableObject.OnExposedToScriptCode(ScriptEngine engine)
        {
            MiscHelpers.VerifyNonNullArgument(engine, nameof(engine));
            this.engine = engine;
        }

        // ReSharper restore ParameterHidesMember

        #endregion
    }
}
