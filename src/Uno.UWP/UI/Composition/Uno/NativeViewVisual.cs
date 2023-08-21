﻿#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Windows.UI;
using Windows.UI.Composition;
using Android.Graphics;
using Android.Views;

namespace Uno.UI.Composition
{
	internal sealed class NativeViewVisual : Visual
	{
		private readonly View _view;
		private readonly bool _preferDrawOnUIThread = false;

		public NativeViewVisual(View view, UIContext context)
			: this(view, GetKindFromWellKnownViewType(view), context)
		{
		}

		public NativeViewVisual(View view, VisualKind kind, UIContext context)
			: base(context.Compositor)
		{
			_view = view;
			view.LayoutChange += (snd, e) =>
			{
				// TODO leak
				Offset = new Vector3(e.Left, e.Top, 0);
				Size = new Vector2(e.Right - e.Left, e.Bottom - e.Top);
				Invalidate(CompositionPropertyType.Dependent);
			};
			Kind = kind;

			Invalidate(CompositionPropertyType.Dependent);
		}

		/// <inheritdoc />
		private protected override void OnCommit()
		{
			base.OnCommit();

			// Even if the Commit should be as fast as possible, this is a balance between perf and functionality.
			// Allowing controls that must be drawn on UI thread to draw in the Commit has a bad perf impact,
			// but allows more native controls to work properly.
			if (_preferDrawOnUIThread)
			{
				RenderDependent();
			}
		}

		/// <inheritdoc />
		private protected override void RenderDependent(Canvas canvas)
		{
			base.RenderDependent(canvas);

			_view.Draw(canvas);
		}

		private static VisualKind GetKindFromWellKnownViewType(View view)
			=> view switch
			{
				//RecyclerView => 
				Android.Webkit.WebView _ => VisualKind.NativeIndependent,
				_ => VisualKind.UnknownNativeView
			};
	}
}
