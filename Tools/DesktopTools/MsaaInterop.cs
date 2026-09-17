using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
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
    private const int MaxSearchDepth = 6;
    private const int MatchTolerance = 2;

    // Read just CHILDID_SELF for an HWND: no UIA bridge or accessibility tree walk.
    public static void DescribeWindow(IntPtr hwnd, ControlInfo info)
    {
      IAccessible accessible = FromWindow(hwnd);
      if (accessible == null) return;
      try
      {
        // Do not treat a grid/list/browser as a plain HWND just because it has one.
        int role = Convert.ToInt32(accessible.get_accRole(0));
        bool simpleClass = info.Role == "text" || info.Role == "edit" || info.Role == "button" ||
                           info.Role == "window" || info.Role == "#32770";
        info.SupportsNativeDiscovery = simpleClass &&
          (role == 9 || role == 10 || role == 16 || role == 20 || role == 41 || role == 42 ||
           role == 43 || role == 44 || role == 45);
      }
      catch { }
      try
      {
        string name = accessible.get_accName(0);
        if (!string.IsNullOrEmpty(name)) info.Label = name;
      }
      catch { }
      try { info.Value = accessible.get_accValue(0) ?? ""; }
      catch { }
    }

    public static bool TryDefaultActionHwnd(IntPtr hwnd)
    {
      IAccessible accessible = FromWindow(hwnd);
      if (accessible == null) return false;
      try
      {
        if (string.IsNullOrEmpty(accessible.get_accDefaultAction(0))) return false;
        return RunTimed(delegate { accessible.accDoDefaultAction(0); }, 2000);
      }
      catch { return false; }
    }

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
      if (containerHwnd == IntPtr.Zero || control == null)
        return false;

      // Synthesized grid rows (XP DataGridView) are matched by name/value — cached bounds
      // are often off-screen until after accSelect scrolls them into view.
      if (control.Element == null &&
          (!string.IsNullOrEmpty(control.Label) || !string.IsNullOrEmpty(control.Value)))
      {
        if (TryActivateByIdentity(containerHwnd, control))
          return true;
      }

      if (!control.HasLocation)
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

    /// <summary>
    /// Scroll an MSAA-only control (e.g. DataGridView row) into view via accSelect, then
    /// refresh cached screen bounds. UIA ScrollItemPattern is unavailable for these rows.
    /// </summary>
    public static bool TryEnsureInView(ControlInfo control)
    {
      if (control == null)
        return false;

      IntPtr hwnd = control.Hwnd;
      if (hwnd == IntPtr.Zero)
        return false;

      IAccessible owner;
      int childId;
      if (!TryFindByIdentity(hwnd, control, out owner, out childId))
        return false;

      if (!TrySelect(owner, childId))
        return false;

      // Virtualized grids need a tick to move the row into the client area.
      Thread.Sleep(50);

      Bounds bounds;
      if (!TryGetBounds(owner, childId, out bounds))
        return true;

      control.X = bounds.Left;
      control.Y = bounds.Top;
      control.Width = bounds.Width;
      control.Height = bounds.Height;
      control.HasLocation = true;
      return true;
    }

    private static bool TryActivateByIdentity(IntPtr containerHwnd, ControlInfo control)
    {
      IAccessible owner;
      int childId;
      if (!TryFindByIdentity(containerHwnd, control, out owner, out childId))
        return false;

      return TryActivate(owner, childId);
    }

    private static bool TryFindByIdentity(
      IntPtr hwnd, ControlInfo control, out IAccessible owner, out int childId)
    {
      owner = null;
      childId = 0;

      IAccessible root = FromWindow(hwnd);
      if (root == null)
        return false;

      string label = control.Label ?? "";
      string value = control.Value ?? "";

      foreach (AccessibleChild child in EnumerateChildren(root))
      {
        string name = "";
        string childValue = "";
        try { name = child.Owner.get_accName(child.Id) ?? ""; }
        catch { }
        try { childValue = child.Owner.get_accValue(child.Id) ?? ""; }
        catch { }

        bool labelMatch = !string.IsNullOrEmpty(label) &&
                          string.Equals(name, label, StringComparison.Ordinal);
        bool valueMatch = !string.IsNullOrEmpty(value) &&
                          (string.Equals(childValue, value, StringComparison.Ordinal) ||
                           string.Equals(name, value, StringComparison.Ordinal));

        if (!labelMatch && !valueMatch)
          continue;

        owner = child.Owner;
        childId = child.Id;
        return true;
      }

      return false;
    }

    private static bool TrySelect(IAccessible owner, int childId)
    {
      object child = childId;
      try
      {
        owner.accSelect(SELFLAG_TAKEFOCUS | SELFLAG_TAKESELECTION, child);
        return true;
      }
      catch
      {
      }

      try
      {
        owner.accSelect(SELFLAG_TAKESELECTION, child);
        return true;
      }
      catch
      {
        return false;
      }
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

      foreach (AccessibleChild child in EnumerateChildren(parent))
      {
        Bounds bounds;
        if (!TryGetBounds(child.Owner, child.Id, out bounds))
          continue;

        if (Matches(bounds, target) && NameMatches(child.Owner, child.Id, name))
        {
          owner = child.Owner;
          childId = child.Id;
          return true;
        }

        if (child.Node != null && Encloses(bounds, target) &&
            TryFind(child.Node, target, name, depth + 1, out owner, out childId))
          return true;
      }

      return false;
    }

    private static bool TryActivate(IAccessible owner, int childId)
    {
      object child = childId;

      // accSelect also scrolls virtualized DataGridView rows into view on XP.
      if (TrySelect(owner, childId))
        return true;

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

    /// <summary>
    /// List DataGridView (and similar) rows via MSAA when UIA GridPattern is unavailable (e.g. XP).
    /// </summary>
    public static int TryAppendGridRows(IntPtr hwnd, int depth, List<ControlInfo> controls)
    {
      if (hwnd == IntPtr.Zero || controls == null)
        return 0;

      IAccessible root = FromWindow(hwnd);
      if (root == null)
        return 0;

      int added = 0;
      int rowIndex = 0;
      foreach (AccessibleChild child in EnumerateChildren(root))
      {
        int role = 0;
        string name = "";
        string value = "";
        try
        {
          object rawRole = child.Owner.get_accRole(child.Id);
          role = rawRole is int ? (int)rawRole : Convert.ToInt32(rawRole);
        }
        catch { }
        try { name = child.Owner.get_accName(child.Id) ?? ""; }
        catch { }
        try { value = child.Owner.get_accValue(child.Id) ?? ""; }
        catch { }

        if (IsScrollOrChromeRole(role, name))
          continue;

        bool isHeader = role == RoleColumnHeader ||
                        (!string.IsNullOrEmpty(name) &&
                         name.Equals("Top Row", StringComparison.OrdinalIgnoreCase));
        bool isRow = role == RoleRow ||
                     (!string.IsNullOrEmpty(name) &&
                      name.StartsWith("Row ", StringComparison.OrdinalIgnoreCase));
        bool looksLikeData = !isHeader &&
                             (isRow ||
                              (!string.IsNullOrEmpty(value) && value.IndexOf(';') >= 0) ||
                              (!string.IsNullOrEmpty(name) && name.IndexOf(';') >= 0));

        if (!isHeader && !looksLikeData && !isRow)
          continue;

        var info = new ControlInfo
        {
          Depth = depth,
          Role = isHeader ? "header" : "custom",
          Label = string.IsNullOrEmpty(name)
            ? (isHeader ? "Top Row" : ("Row " + rowIndex))
            : name,
          Value = value ?? "",
          // Grid HWND so navigate/EnsureInView can re-bind via MSAA (no AutomationElement).
          Hwnd = hwnd,
          Enabled = true
        };

        if (!isHeader && string.IsNullOrEmpty(info.Value) && !string.IsNullOrEmpty(name) &&
            name.IndexOf(';') >= 0)
        {
          info.Value = name;
          if (info.Label.IndexOf(';') >= 0)
            info.Label = "Row " + rowIndex;
        }

        Bounds bounds;
        if (TryGetBounds(child.Owner, child.Id, out bounds))
        {
          info.X = bounds.Left;
          info.Y = bounds.Top;
          info.Width = bounds.Width;
          info.Height = bounds.Height;
          info.HasLocation = true;
        }

        controls.Add(info);
        added++;
        if (!isHeader)
          rowIndex++;
      }

      return added;
    }

    private const int RoleScrollbar = 0x03;
    private const int RoleColumnHeader = 0x19;
    private const int RoleRow = 0x1C;
    private const int RoleSeparator = 0x15;
    private const int RoleGrip = 0x04;

    private static bool IsScrollOrChromeRole(int role, string name)
    {
      if (role == RoleScrollbar || role == RoleSeparator || role == RoleGrip)
        return true;
      if (string.IsNullOrEmpty(name))
        return false;
      return name.IndexOf("Scroll Bar", StringComparison.OrdinalIgnoreCase) >= 0 ||
             name.IndexOf("Scrollbar", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private struct AccessibleChild
    {
      public IAccessible Owner;
      public IAccessible Node;
      public int Id;
    }

    private static IEnumerable<AccessibleChild> EnumerateChildren(IAccessible parent)
    {
      object[] children = GetChildren(parent);
      if (children == null)
        yield break;

      foreach (object child in children)
      {
        IAccessible node = child as IAccessible;
        int id = node == null ? ToChildId(child) : 0;
        if (node != null || id != 0)
          yield return new AccessibleChild { Owner = node ?? parent, Node = node, Id = id };
      }
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
