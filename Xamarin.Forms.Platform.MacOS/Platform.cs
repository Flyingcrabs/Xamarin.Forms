﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AppKit;
using RectangleF = CoreGraphics.CGRect;
using System.Linq;

namespace Xamarin.Forms.Platform.MacOS
{
	public class Platform : BindableObject, IPlatform, INavigation, IDisposable
	{
		internal static readonly BindableProperty RendererProperty = BindableProperty.CreateAttached("Renderer", typeof(IVisualElementRenderer), typeof(Platform), default(IVisualElementRenderer),
				propertyChanged: (bindable, oldvalue, newvalue) =>
				{
					var view = bindable as VisualElement;
					if (view != null)
						view.IsPlatformEnabled = newvalue != null;
				});

		readonly PlatformRenderer _renderer;
		readonly ModalPageTracker _modalTracker;
		bool _animateModals = true;
		bool _appeared;
		bool _disposed;

		internal Platform()
		{
			_renderer = new PlatformRenderer(this);
			_modalTracker = new ModalPageTracker(_renderer);

			MessagingCenter.Subscribe(this, Page.AlertSignalName, (Page sender, AlertArguments arguments) =>
			{
				if (!PageIsChildOfPlatform(sender))
					return;

				var alert = NSAlert.WithMessage(arguments.Title, arguments.Cancel, arguments.Accept, null, arguments.Message);
				var result = alert.RunModal();
				arguments.SetResult(result == 1);
			});

			MessagingCenter.Subscribe(this, Page.ActionSheetSignalName, (Page sender, ActionSheetArguments arguments) =>
			{
				if (!PageIsChildOfPlatform(sender))
					return;

				var alert = NSAlert.WithMessage(arguments.Title, arguments.Cancel, arguments.Destruction, null, "");
				if (arguments.Buttons != null)
				{
					alert.AccessoryView = GetExtraButton(arguments);
					alert.Layout();
				}

				var result = (int)alert.RunSheetModal(NSApplication.SharedApplication.MainWindow);
				var titleResult = string.Empty;
				if (result == 1)
					titleResult = arguments.Cancel;
				else if (result == 0)
					titleResult = arguments.Destruction;
				else if (result > 1 && arguments.Buttons != null && result - 2 <= arguments.Buttons.Count())
					titleResult = arguments.Buttons.ElementAt(result - 2);

				arguments.SetResult(titleResult);
			});
		}

		void INavigation.InsertPageBefore(Page page, Page before)
		{
			throw new InvalidOperationException("InsertPageBefore is not supported globally on MacOS, please use a NavigationPage.");
		}

		IReadOnlyList<Page> INavigation.ModalStack
		{
			get { return _modalTracker.ModalStack; }
		}

		IReadOnlyList<Page> INavigation.NavigationStack
		{
			get { return new List<Page>(); }
		}

		Task<Page> INavigation.PopAsync()
		{
			return ((INavigation)this).PopAsync(true);
		}

		Task<Page> INavigation.PopAsync(bool animated)
		{
			throw new InvalidOperationException("PopAsync is not supported globally on MacOS, please use a NavigationPage.");
		}

		Task INavigation.PopToRootAsync()
		{
			return ((INavigation)this).PopToRootAsync(true);
		}

		Task INavigation.PopToRootAsync(bool animated)
		{
			throw new InvalidOperationException("PopToRootAsync is not supported globally on MacOS, please use a NavigationPage.");
		}

		Task INavigation.PushAsync(Page root)
		{
			return ((INavigation)this).PushAsync(root, true);
		}

		Task INavigation.PushAsync(Page root, bool animated)
		{
			throw new InvalidOperationException("PushAsync is not supported globally on MacOS, please use a NavigationPage.");
		}

		Task INavigation.PushModalAsync(Page modal)
		{
			return ((INavigation)this).PushModalAsync(modal, true);
		}

		Task<Page> INavigation.PopModalAsync()
		{
			return ((INavigation)this).PopModalAsync(true);
		}

		Task INavigation.PushModalAsync(Page modal, bool animated)
		{
			modal.Platform = this;

			if (_appeared)
				return _modalTracker.PushAsync(modal, _animateModals && animated);
			return Task.FromResult<object>(null);
		}

		Task<Page> INavigation.PopModalAsync(bool animated)
		{
			return _modalTracker.PopAsync(animated);
		}

		void INavigation.RemovePage(Page page)
		{
			throw new InvalidOperationException("RemovePage is not supported globally on iOS, please use a NavigationPage.");
		}

		SizeRequest IPlatform.GetNativeSize(VisualElement view, double widthConstraint, double heightConstraint)
		{
			var renderView = GetRenderer(view);
			if (renderView == null || renderView.NativeView == null)
				return new SizeRequest(Size.Zero);

			return renderView.GetDesiredSize(widthConstraint, heightConstraint);
		}

		internal NSViewController ViewController
		{
			get { return _renderer; }
		}

		Page Page { get; set; }

		Application TargetApplication
		{
			get
			{
				if (Page == null)
					return null;
				return Page.RealParent as Application;
			}
		}

		void IDisposable.Dispose()
		{
			if (_disposed)
				return;
			_disposed = true;

			Page.DescendantRemoved -= HandleChildRemoved;
			MessagingCenter.Unsubscribe<Page, ActionSheetArguments>(this, Page.ActionSheetSignalName);
			MessagingCenter.Unsubscribe<Page, AlertArguments>(this, Page.AlertSignalName);
			MessagingCenter.Unsubscribe<Page, bool>(this, Page.BusySetSignalName);

			DisposeModelAndChildrenRenderers(Page);

			_modalTracker.Dispose();
			_renderer.Dispose();
		}

		public static IVisualElementRenderer CreateRenderer(VisualElement element)
		{
			var t = element.GetType();
			var renderer = Registrar.Registered.GetHandler<IVisualElementRenderer>(t) ?? new DefaultRenderer();
			renderer.SetElement(element);
			return renderer;
		}

		public static IVisualElementRenderer GetRenderer(VisualElement bindable)
		{
			return (IVisualElementRenderer)bindable.GetValue(RendererProperty);
		}

		public static void SetRenderer(VisualElement bindable, IVisualElementRenderer value)
		{
			bindable.SetValue(RendererProperty, value);
		}

		protected override void OnBindingContextChanged()
		{
			SetInheritedBindingContext(Page, BindingContext);

			base.OnBindingContextChanged();
		}

		internal static void DisposeModelAndChildrenRenderers(Element view)
		{
			IVisualElementRenderer renderer;
			foreach (VisualElement child in view.Descendants())
				DisposeModelAndChildrenRenderers(child);

			renderer = GetRenderer((VisualElement)view);
			renderer?.ViewController?.RemoveFromParentViewController();

			renderer?.NativeView?.RemoveFromSuperview();
			renderer?.Dispose();

			view.ClearValue(RendererProperty);
		}

		internal static void DisposeRendererAndChildren(IVisualElementRenderer rendererToRemove)
		{
			if (rendererToRemove == null)
				return;

			if (rendererToRemove.Element != null && GetRenderer(rendererToRemove.Element) == rendererToRemove)
				rendererToRemove.Element.ClearValue(RendererProperty);

			var subviews = rendererToRemove.NativeView.Subviews;
			for (var i = 0; i < subviews.Length; i++)
			{
				var childRenderer = subviews[i] as IVisualElementRenderer;
				if (childRenderer != null)
					DisposeRendererAndChildren(childRenderer);
			}

			rendererToRemove.NativeView.RemoveFromSuperview();
			rendererToRemove.Dispose();
		}

		internal void LayoutSubviews()
		{
			if (Page == null)
				return;

			var rootRenderer = GetRenderer(Page);

			if (rootRenderer == null)
				return;

			rootRenderer.SetElementSize(new Size(_renderer.View.Bounds.Width, _renderer.View.Bounds.Height));
		}

		internal void SetPage(Page newRoot)
		{
			if (newRoot == null)
				return;
			if (Page != null)
				throw new NotImplementedException();
			Page = newRoot;

			if (_appeared == false)
				return;

			Page.Platform = this;
			AddChild(Page);

			Page.DescendantRemoved += HandleChildRemoved;

			TargetApplication.NavigationProxy.Inner = this;
		}

		internal void DidAppear()
		{
			_animateModals = false;
			TargetApplication.NavigationProxy.Inner = this;
			_animateModals = true;
		}

		internal void WillAppear()
		{
			if (_appeared)
				return;

			Page.Platform = this;
			AddChild(Page);

			Page.DescendantRemoved += HandleChildRemoved;

			_appeared = true;
		}

		static FormsNSView GetExtraButton(ActionSheetArguments arguments)
		{
			var newView = new FormsNSView();
			int height = 50;
			int width = 300;
			int i = 0;
			foreach (var button in arguments.Buttons)
			{
				var btn = new NSButton { Title = button, Tag = i };
				btn.SetButtonType(NSButtonType.MomentaryPushIn);
				btn.Activated += (s, e) =>
				{
					NSApplication.SharedApplication.EndSheet(NSApplication.SharedApplication.MainWindow.AttachedSheet, ((NSButton)s).Tag + 2);
				};
				btn.Frame = new RectangleF(0, height * i, width, height);
				newView.AddSubview(btn);
				i++;
			}
			newView.Frame = new RectangleF(0, 0, width, height * i);
			return newView;
		}

		bool PageIsChildOfPlatform(Page page)
		{
			while (!Application.IsApplicationOrNull(page.RealParent))
				page = (Page)page.RealParent;

			return Page == page || _modalTracker.ModalStack.Contains(page);
		}

		void AddChild(VisualElement view)
		{
			if (!Application.IsApplicationOrNull(view.RealParent))
				Console.Error.WriteLine("Tried to add parented view to canvas directly");

			if (GetRenderer(view) == null)
			{
				var viewRenderer = CreateRenderer(view);
				SetRenderer(view, viewRenderer);

				_renderer.View.AddSubview(viewRenderer.NativeView);
				if (viewRenderer.ViewController != null)
					_renderer.AddChildViewController(viewRenderer.ViewController);
				viewRenderer.SetElementSize(new Size(_renderer.View.Bounds.Width, _renderer.View.Bounds.Height));
			}
			else
				Console.Error.WriteLine("Potential view double add");
		}

		void HandleChildRemoved(object sender, ElementEventArgs e)
		{
			var view = e.Element;
			DisposeModelAndChildrenRenderers(view);
		}
	}
}

