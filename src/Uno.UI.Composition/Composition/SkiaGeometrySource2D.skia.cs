﻿#nullable enable

using SkiaSharp;
using System;
using Windows.Graphics;

namespace Microsoft.UI.Composition
{
	public class SkiaGeometrySource2D : IGeometrySource2D, IDisposable
	{
		public SkiaGeometrySource2D(SKPath source)
		{
			Geometry = source ?? throw new ArgumentNullException(nameof(source));
		}

		/// <remarks>
		/// DO NOT MODIFY THIS SKPath. CREATE A NEW SkiaGeometrySource2D INSTEAD.
		/// This can lead to nasty invalidation bugs where the SKPath changes without notifying anyone.
		/// </remarks>
		public SKPath Geometry { get; }

		public void Dispose() => Geometry.Dispose();
	}
}
