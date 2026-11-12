using System;
using UnityEngine;

namespace TarmacGauntlet.Visuals
{
    /// <summary>
    /// One graphics option the settings menu shows a row for: on or off, or a count from 0 to <see cref="Most"/>. A change
    /// is saved at once and remembered between runs, and <see cref="Changed"/> tells whatever draws the effect so it can
    /// react live. Declare one in <see cref="GraphicsOptions"/>.
    /// </summary>
    public sealed class GraphicsOption
    {
        /// <summary>The PlayerPrefs key it is saved under.</summary>
        public readonly string Key;
        /// <summary>The settings row's label.</summary>
        public readonly string Label;
        /// <summary>The value before anyone sets it.</summary>
        public readonly int Default;
        /// <summary>The highest value: 1 for an on/off option.</summary>
        public readonly int Most;

        /// <summary>After the value changes, with the option.</summary>
        public event Action<GraphicsOption> Changed;

        internal GraphicsOption(string key, string label, int defaultValue, int most)
        {
            Key = key;
            Label = label;
            Most = Mathf.Max(1, most);
            Default = Mathf.Clamp(defaultValue, 0, Most);
        }

        /// <summary>True for an on/off option, false for a count.</summary>
        public bool IsToggle => Most == 1;

        public int Value
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(Key, Default), 0, Most);
            set
            {
                int next = Mathf.Clamp(value, 0, Most);
                if (next == Value && PlayerPrefs.HasKey(Key)) return;
                PlayerPrefs.SetInt(Key, next);
                PlayerPrefs.Save();
                Changed?.Invoke(this);
            }
        }

        public bool On
        {
            get => Value > 0;
            set => Value = value ? Most : 0;
        }
    }
}
