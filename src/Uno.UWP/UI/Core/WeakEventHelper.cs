﻿#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Uno.Buffers;
using Uno.Disposables;
using Uno.Extensions;
using Windows.Security.Cryptography.Core;

namespace Windows.UI.Core
{
	internal class WeakEventHelper
	{
		/// <summary>
		/// Defines a event raise method.
		/// </summary>
		/// <param name="delegate">The delegate to call with <paramref name="source"/> and <paramref name="args"/>.</param>
		/// <param name="source">The source of the event raise</param>
		/// <param name="args">The args used to raise the event</param>
		public delegate void EventRaiseHandler(Delegate @delegate, object source, object? args);

		/// <summary>
		/// An abstract handler to be called when raising the weak event.
		/// </summary>
		/// <param name="source">The source of the event</param>
		/// <param name="args">
		/// The args of the event, which must match the explicit cast
		/// performed in the matching <see cref="EventRaiseHandler"/> instance.
		/// </param>
		public delegate void GenericEventHandler(object source, object? args);

		/// <summary>
		/// Provides an abstraction for GC trimming registration
		/// </summary>
		internal interface ITrimProvider
		{
			/// <summary>
			/// Registers a callback to be called when GC triggered
			/// </summary>
			/// <param name="callback">Function called with <paramref name="target"/> as the first parameter</param>
			/// <param name="target">instance to be provided when invoking <paramref name="callback"/></param>
			void RegisterTrimCallback(Func<object, bool> callback, object target);
		}

		/// <summary>
		/// Default trimming implementation which uses the actual GC implementation
		/// </summary>
		private class DefaultTrimProvider : ITrimProvider
		{
			public void RegisterTrimCallback(Func<object, bool> callback, object target)
			{
				Windows.Foundation.Gen2GcCallback.Register(callback, target);
			}
		}

		/// <summary>
		/// A collection of weak event registrations
		/// </summary>
		public class WeakEventCollection : IDisposable
		{
			private record WeakHandler(WeakReference Target, GenericEventHandler Handler);

			private object _lock = new object();
			private List<WeakHandler> _handlers = [];
			private readonly ITrimProvider _provider;

			public WeakEventCollection(ITrimProvider? provider = null)
			{
				_provider = provider ?? new DefaultTrimProvider();
			}

			private bool Trim()
			{
				lock (_lock)
				{
					for (int i = 0; i < _handlers.Count; i++)
					{
						if (!_handlers[i].Target.IsAlive)
						{
							_handlers.RemoveAt(i);
							i--;
						}
					}

					return _handlers.Count != 0;
				}
			}

			/// <summary>
			/// Invokes all the alive registered handlers
			/// </summary>
			public void Invoke(object sender, object? args)
			{
				lock (_lock)
				{
					for (int i = 0; i < _handlers.Count; i++)
					{
						_handlers[i].Handler(sender, args);
					}
				}
			}

			/// <summary>
			/// Do not use directly, use <see cref="RegisterEvent"/> instead.
			/// Registers an handler to be called when the event is raised. 
			/// </summary>
			/// <returns>A disposable that can be used to unregister the provided handler. The disposable instance is not tracked by the GC and will not collect the registration.</returns>
			internal IDisposable Register(WeakReference target, GenericEventHandler handler)
			{
				lock (_lock)
				{
					WeakHandler key = new(target, handler);
					_handlers.Add(key);

					if (_handlers.Count == 1)
					{
						_provider.RegisterTrimCallback(_ => Trim(), this);
					}

					return Disposable.Create(RemoveRegistration);

					void RemoveRegistration()
					{
						lock (_lock)
						{
							_handlers.Remove(key);
						}
					}
				}
			}

			public void Dispose()
			{
				lock (_lock)
				{
					_handlers.Clear();
				}
			}
		}

		/// <summary>
		/// Provides a bi-directional weak event handler management.
		/// </summary>
		/// <param name="list">A list of registrations to manage</param>
		/// <param name="handler">The actual handler to execute.</param>
		/// <param name="raise">The delegate used to raise <paramref name="handler"/> if it has not been collected.</param>
		/// <returns>A disposable that keeps the registration alive.</returns>
		/// <remarks>
		/// The bi-directional relation is defined by the fact that both the 
		/// source and the target are weak. The source must be kept alive by 
		/// another longer-lived reference, and the target is kept alive by the
		/// return disposable.
		/// 
		/// If either the <paramref name="list"/> or the <paramref name="handler"/> are 
		/// collected, raising the event will produce nothing.
		/// 
		/// The returned disposable instance itself is not tracked by the GC and will not collect the registration.
		/// </remarks>
		internal static IDisposable RegisterEvent(WeakEventCollection list, Delegate handler, EventRaiseHandler raise)
		{
			var wr = new WeakReference(handler);

			GenericEventHandler? genericHandler = null;

			// This weak reference ensure that the closure will not link
			// the caller and the callee, in the same way "newValueActionWeak" 
			// does not link the callee to the caller.
			var instanceRef = new WeakReference<WeakEventCollection>(list);

			genericHandler = (s, e) =>
			{
				var weakHandler = wr.Target as Delegate;

				if (weakHandler != null)
				{
					raise(weakHandler, s, e);
				}
			};

			return list.Register(wr, genericHandler);
		}
	}
}
