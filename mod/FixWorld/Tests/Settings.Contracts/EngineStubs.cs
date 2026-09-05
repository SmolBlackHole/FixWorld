using System;
using System.Collections.Generic;

// Exercise the real settings renderer/state, not Unity's rendering implementation.
namespace UnityEngine
{
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float xMax => x + width;
        public float yMax => y + height;
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Color { public Color(float r, float g, float b, float a = 1) { } }
    public enum TextAnchor { MiddleLeft }
    public enum EventType { KeyUp, Repaint }
    public enum KeyCode { Return, KeypadEnter }
    public sealed class Event { public static Event current = new(); public EventType type = EventType.Repaint; public KeyCode keyCode; }
    public static class GUI
    {
        public static Color color;
        public static string Focus, NextControl;
        public static void BeginGroup(Rect rect) { }
        public static void EndGroup() { }
        public static void SetNextControlName(string name) { NextControl = name; }
        public static string GetNameOfFocusedControl() => Focus;
    }
}
namespace Verse
{
    using UnityEngine;
    public enum GameFont { Small, Medium, Tiny }
    public static class Text { public static GameFont Font; }
    public static class GenUI { public static void SetLabelAlign(TextAnchor anchor) { } public static void ResetLabelAlign() { } }
    public static class Mouse { public static bool IsOver(Rect rect) => false; }
    public static class Widgets
    {
        public static string Input;
        public static void BeginScrollView(Rect visible, ref Vector2 scroll, Rect total) { }
        public static void EndScrollView() { }
        public static void DrawHighlight(Rect rect) { }
        public static void Label(Rect rect, string text) { }
        public static void DrawLineHorizontal(float x, float y, float width) { }
        public static void DrawBox(Rect rect) { }
        public static string TextField(Rect rect, string value)
        { if (Input == null) return value; var input = Input; Input = null; GUI.Focus = GUI.NextControl; return input; }
        public static bool ButtonText(Rect rect, string text) => false;
        public static void Checkbox(float x, float y, ref bool value) { }
    }
    public static class Extensions
    {
        public static bool NullOrEmpty(this string text) => string.IsNullOrEmpty(text);
        public static string Translate(this string text, params object[] args) => text;
        public static bool CanTranslate(this string text) => true;
        public static string Join(this IEnumerable<string> items, string separator) => string.Join(separator, items);
        public static Rect ContractedBy(this Rect r, float padding) => new(r.x + padding, r.y + padding, r.width - padding * 2, r.height - padding * 2);
    }
    public sealed class FloatMenuOption { public FloatMenuOption(string label, Action action) { } }
    public static class Messages { public static void Message(string text, object type) { } }
    public static class Find { public static readonly Stack WindowStack = new(); }
    public sealed class Stack { public object Last; public void Add(object value) { Last = value; } }
}
namespace RimWorld { public static class MessageTypeDefOf { public static readonly object TaskCompletion = new(); } }
namespace FixWorld.Utils
{
    public sealed class Dialog_Confirm
    { public readonly Action Confirm; public Dialog_Confirm(string message, Action confirm, bool destructive) { Confirm = confirm; } }
}
namespace FixWorld.Core
{
    internal static class PersistentDataManager { internal static bool IsValidElementName(string name) => !string.IsNullOrWhiteSpace(name); }
}
namespace FixWorld.Settings
{
    using UnityEngine;
    public sealed class ContextMenuEntry { }
    internal sealed class ModSettingsManager { internal void SaveChanges() { } }
    public sealed class TextMeasurementCache { public float Height(string text, float width, Verse.GameFont font) => 20; }
    public static class ModSettingsWidgets
    {
        public const float HoverMenuHeight = 20;
        public static bool DrawHoverMenuButton(Vector2 point, bool enabled, bool extra) => false;
        public static bool DrawHandleHoverMenu(Vector2 point, string description, bool enabled, bool extra) => false;
        public static void OpenExtensibleContextMenu(string label, Action reset, Action changed, IEnumerable<ContextMenuEntry> entries) { }
        public static void OpenFloatMenu(List<Verse.FloatMenuOption> options) { }
    }
}
namespace FixWorld
{
    public sealed class FixWorldController
    {
        public static readonly FixWorldController Instance = new();
        public static readonly TestLogger Logger = new();
        public readonly Settings.TextMeasurementCache TextMeasurements = new();
    }
    public sealed class TestLogger
    {
        public int Errors;
        public void Warning(string message) { }
        public void Error(string message, params object[] args) { Errors++; }
        public void ReportException(Exception error, params object[] args) { Errors++; }
    }
}
