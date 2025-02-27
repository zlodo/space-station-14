using System;
using System.Collections.Generic;
using System.Threading;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
using Timer = Robust.Shared.Timing.Timer;
namespace Content.Client.ContextMenu.UI
{
    /// <summary>
    ///     This class handles all the logic associated with showing a context menu.
    /// </summary>
    /// <remarks>
    ///     This largely involves setting up timers to open and close sub-menus when hovering over other menu elements.
    /// </remarks>
    public class ContextMenuPresenter : IDisposable
    {
        public static readonly TimeSpan HoverDelay = TimeSpan.FromSeconds(0.2);

        public ContextMenuPopup RootMenu;
        public Stack<ContextMenuPopup> Menus { get; } = new();

        /// <summary>
        ///     Used to cancel the timer that opens menus.
        /// </summary>
        public CancellationTokenSource? CancelOpen;

        /// <summary>
        ///     Used to cancel the timer that closes menus.
        /// </summary>
        public CancellationTokenSource? CancelClose;

        public ContextMenuPresenter()
        {
            RootMenu = new(this, null);
            RootMenu.OnPopupHide += RootMenu.MenuBody.DisposeAllChildren;
            Menus.Push(RootMenu);
        }

        /// <summary>
        ///     Dispose of all UI elements.
        /// </summary>
        public virtual void Dispose()
        {
            RootMenu.OnPopupHide -= RootMenu.MenuBody.DisposeAllChildren;
            RootMenu.Dispose();
        }

        /// <summary>
        ///     Close and clear the root menu. This will also dispose any sub-menus.
        /// </summary>
        public virtual void Close()
        {
            RootMenu.Close();
            CancelOpen?.Cancel();
            CancelClose?.Cancel();
        }

        /// <summary>
        ///     Starts closing menus until the top-most menu is the given one.
        /// </summary>
        /// <remarks>
        ///     Note that this does not actually check if the given menu IS a sub menu of this presenter. In that case
        ///     this will close all menus.
        /// </remarks>
        public void CloseSubMenus(ContextMenuPopup? menu)
        {
            if (menu == null || !menu.Visible)
                return;

            while (Menus.TryPeek(out var subMenu) && subMenu != menu)
            {
                Menus.Pop().Close();
            }
        }

        /// <summary>
        ///     Start a timer to open this element's sub-menu.
        /// </summary>
        public virtual void OnMouseEntered(ContextMenuElement element)
        {
            var topMenu = Menus.Peek();

            if (element.ParentMenu == topMenu || element.SubMenu == topMenu)
                CancelClose?.Cancel();

            if (element.SubMenu == topMenu)
                return;

            // open the sub-menu after a short delay.
            CancelOpen?.Cancel();
            CancelOpen = new();
            Timer.Spawn(HoverDelay, () => OpenSubMenu(element), CancelOpen.Token);
        }

        /// <summary>
        ///     Start a timer to close this element's sub-menu.
        /// </summary>
        /// <remarks>
        ///     Note that this timer will be aborted when entering the actual sub-menu itself.
        /// </remarks>
        public virtual void OnMouseExited(ContextMenuElement element)
        {
            CancelOpen?.Cancel();

            if (element.SubMenu == null)
                return;

            CancelClose?.Cancel();
            CancelClose = new();
            Timer.Spawn(HoverDelay, () => CloseSubMenus(element.ParentMenu), CancelClose.Token);
        }

        public virtual void OnKeyBindDown(ContextMenuElement element, GUIBoundKeyEventArgs args) { }

        /// <summary>
        ///     Opens a new sub menu, and close the old one.
        /// </summary>
        /// <remarks>
        ///     If the given element has no sub-menu, just close the current one.
        /// </remarks>
        public virtual void OpenSubMenu(ContextMenuElement element)
        {
            // If This is already the top most menu, do nothing.
            if (element.SubMenu == Menus.Peek())
                return;

            // Was the parent menu closed or disposed before an open timer completed?
            if (element.Disposed || element.ParentMenu == null || !element.ParentMenu.Visible)
                return;

            // Close any currently open sub-menus up to this element's parent menu.
            CloseSubMenus(element.ParentMenu);

            if (element.SubMenu == null)
                return;

            // open pop-up adjacent to the parent element. We want the sub-menu elements to align with this element
            // which depends on the panel container style margins.
            var altPos = element.GlobalPosition;
            var pos = altPos + (element.Width + 2*ContextMenuElement.ElementMargin, - 2*ContextMenuElement.ElementMargin);
            element.SubMenu.Open(UIBox2.FromDimensions(pos, (1, 1)), altPos);
            element.SubMenu.Close();
            element.SubMenu.Open(UIBox2.FromDimensions(pos, (1, 1)), altPos);

            // draw on top of other menus
            element.SubMenu.SetPositionLast();

            Menus.Push(element.SubMenu);
        }

        /// <summary>
        ///     Add an element to a menu and subscribe to GUI events.
        /// </summary>
        public void AddElement(ContextMenuPopup menu, ContextMenuElement element)
        {
            element.OnMouseEntered += _ => OnMouseEntered(element);
            element.OnMouseExited += _ => OnMouseExited(element);
            element.OnKeyBindDown += args => OnKeyBindDown(element, args);
            element.ParentMenu = menu;
            menu.MenuBody.AddChild(element);
            menu.InvalidateMeasure();
        }

        /// <summary>
        ///     Removes event subscriptions when an element is removed from a menu, 
        /// </summary>
        public void OnRemoveElement(ContextMenuPopup menu, Control control)
        {
            if (control is not ContextMenuElement element)
                return;

            element.OnMouseEntered -= _ => OnMouseEntered(element);
            element.OnMouseExited -= _ => OnMouseExited(element);
            element.OnKeyBindDown -= args => OnKeyBindDown(element, args);

            menu.InvalidateMeasure();
        }
    }
}
