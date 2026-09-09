using System;
using System.Runtime.InteropServices;
using Accessibility;

namespace DesktopTools
{
  /// <summary>
  /// Action fallback for elements the UI Automation bridge exposes read-only. Grid rows and
  /// similar virtual children carry no actionable UIA pattern, but the MSAA object underneath
  /// still implements accSelect and accDoDefaultAction, which act without moving the cursor.
  /// </summary>
  internal static class MsaaInterop
  {
    private const int OBJID_CLIENT = unchecked((int)0xFFFFFFFC);
    private const int SELFLAG_TAKEFOCUS = 0x1;
    private const int SELFLAG_TAKESELECTION = 0x2;
    private const int STATE_SELECTABLE = 0x200000;
    private const int MaxSearchDepth = 6;
    private const int MatchTolerance = 2;

    private static readonly Guid IID_IAccessible = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, int objectId, ref Guid iid,
      [MarshalAs(UnmanagedType.Interface)] out object accessible);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleChildren(IAccessible container, int startIndex, int count,
      [Out] object[] children, out int obtained);

    private struct Bounds
    {
      public int Left;
      public int Top;
      public int Width;
      public int Height;
    }

    /// <summary>
    /// Selects or invokes the accessibility object matching <paramref name="control"/>.
    /// False means MSAA could not locate or act on it, and the caller should fall back.
    /// </summary>
    public static bool TryActivate(IntPtr containerHwnd, ControlInfo control)
    {
      if (containerHwnd == IntPtr.Zero || control == null || !control.HasLocation)
        return false;

      IAccessible root = FromWindow(containerHwnd);
      if (root == null)
        return false;

      // ControlInfo holds physical pixels. MSAA answers in the window's own space, which
      // differs only when the target is DPI-unaware, so try both readings.
      double scale = Win32Interop.GetProviderScale(containerHwnd);

      if (TryActivateAt(root, Rescale(control, scale), control.Label))
        return true;

      return scale != 1.0 && TryActivateAt(root, Rescale(control, 1.0), control.Label);
    }

    private static bool TryActivateAt(IAccessible root, Bounds target, string name)
    {
      IAccessible owner;
      int childId;
      if (!TryFind(root, target, name, 0, out owner, out childId))
        return false;

      return TryActivate(owner, childId);
    }

    private static Bounds Rescale(ControlInfo control, double scale)
    {
      return new Bounds
      {
        Left = (int)Math.Round(control.X / scale),
        Top = (int)Math.Round(control.Y / scale),
        Width = (int)Math.Round(control.Width / scale),
        Height = (int)Math.Round(control.Height / scale)
      };
    }

    private static IAccessible FromWindow(IntPtr hwnd)
    {
      try
      {
        Guid iid = IID_IAccessible;
        object raw;
        if (AccessibleObjectFromWindow(hwnd, OBJID_CLIENT, ref iid, out raw) != 0)
          return null;

        return raw as IAccessible;
      }
      catch
      {
        return null;
      }
    }

    /// <summary>
    /// Descends only into children whose bounds enclose the target, so a grid with thousands
    /// of rows costs one scan per level rather than a full tree walk.
    /// </summary>
    private static bool TryFind(IAccessible parent, Bounds target, string name, int depth,
      out IAccessible owner, out int childId)
    {
      owner = null;
      childId = 0;

      if (depth > MaxSearchDepth)
        return false;

      object[] children = GetChildren(parent);
      if (children == null)
        return false;

      foreach (object child in children)
      {
        IAccessible node = child as IAccessible;
        int id = node == null ? ToChildId(child) : 0;
        if (node == null && id == 0)
          continue;

        IAccessible holder = node ?? parent;

        Bounds bounds;
        if (!TryGetBounds(holder, id, out bounds))
          continue;

        if (Matches(bounds, target) && NameMatches(holder, id, name))
        {
          owner = holder;
          childId = id;
          return true;
        }

        if (node != null && Encloses(bounds, target) &&
            TryFind(node, target, name, depth + 1, out owner, out childId))
          return true;
      }

      return false;
    }

    private static bool TryActivate(IAccessible owner, int childId)
    {
      object child = childId;

      int state = 0;
      try
      {
        state = Convert.ToInt32(owner.get_accState(child));
      }
      catch
      {
        // Treated as "not selectable"; the default action below still gets a chance.
      }

      if ((state & STATE_SELECTABLE) != 0)
      {
        try
        {
          owner.accSelect(SELFLAG_TAKEFOCUS | SELFLAG_TAKESELECTION, child);
          return true;
        }
        catch
        {
        }
      }

      try
      {
        if (!string.IsNullOrEmpty(owner.get_accDefaultAction(child)))
        {
          // accDoDefaultAction blocks if the action shows a modal dialog.
          return RunTimed(delegate { owner.accDoDefaultAction(child); }, 2000);
        }
      }
      catch
      {
      }

      return false;
    }

    private static bool RunTimed(Action action, int timeoutMs)
    {
      // Timed out → treat as success so callers do not fall through to another activate.
      StaTimeout.Result result = StaTimeout.Run(action, timeoutMs, preferSta: false);
      return result == StaTimeout.Result.Succeeded || result == StaTimeout.Result.TimedOut;
    }

    private static object[] GetChildren(IAccessible parent)
    {
      try
      {
        int count = parent.accChildCount;
        if (count <= 0)
          return null;

        var children = new object[count];
        int obtained;
        if (AccessibleChildren(parent, 0, count, children, out obtained) != 0)
          return null;

        if (obtained < count)
          Array.Resize(ref children, obtained);

        return children;
      }
      catch
      {
        return null;
      }
    }

    private static int ToChildId(object child)
    {
      try
      {
        return child == null ? 0 : Convert.ToInt32(child);
      }
      catch
      {
        return 0;
      }
    }

    private static bool TryGetBounds(IAccessible holder, int childId, out Bounds bounds)
    {
      bounds = new Bounds();

      try
      {
        holder.accLocation(out bounds.Left, out bounds.Top, out bounds.Width, out bounds.Height, childId);
        return bounds.Width > 0 && bounds.Height > 0;
      }
      catch
      {
        return false;
      }
    }

    private static bool NameMatches(IAccessible holder, int childId, string name)
    {
      if (string.IsNullOrEmpty(name))
        return true;

      try
      {
        return string.Equals(holder.get_accName(childId), name, StringComparison.Ordinal);
      }
      catch
      {
        return false;
      }
    }

    private static bool Matches(Bounds bounds, Bounds target)
    {
      return Near(bounds.Left, target.Left) &&
             Near(bounds.Top, target.Top) &&
             Near(bounds.Width, target.Width) &&
             Near(bounds.Height, target.Height);
    }

    private static bool Encloses(Bounds outer, Bounds inner)
    {
      return outer.Left <= inner.Left + MatchTolerance &&
             outer.Top <= inner.Top + MatchTolerance &&
             outer.Left + outer.Width >= inner.Left + inner.Width - MatchTolerance &&
             outer.Top + outer.Height >= inner.Top + inner.Height - MatchTolerance;
    }

    private static bool Near(int a, int b)
    {
      int delta = a - b;
      return delta >= -MatchTolerance && delta <= MatchTolerance;
    }
  }
}
