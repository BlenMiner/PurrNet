﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Octokit
{
    /// <summary>
    /// Describes a new label to create via the <see cref="IIssuesLabelsClient.Create(string,string,NewLabel)"/> method.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public class NewLabel
    {
        private string _color;

        /// <summary>
        /// Initializes a new instance of the <see cref="NewLabel"/> class.
        /// </summary>
        /// <param name="name">The name of the label.</param>
        /// <param name="color">The color of the label.</param>
        public NewLabel(string name, string color)
        {
            Ensure.ArgumentNotNullOrEmptyString(name, nameof(name));
            Ensure.ArgumentNotNullOrEmptyString(color, nameof(color));

            Name = name;
            Color = color;
        }

        /// <summary>
        /// Name of the label (required).
        /// </summary>
        /// <remarks>
        /// Emoji can be added to label names, using either native emoji or colon-style markup. For example,
        /// typing :strawberry: will render the emoji for strawberry. For a full list of available emoji and codes, see http://emoji-cheat-sheet.com/.
        /// </remarks>
        public string Name { get; set; }

        /// <summary>
        /// Color of the label (required).
        /// </summary>
        /// <remarks>
        /// The hexadecimal color code for the label, without the leading #.
        /// </remarks>
        public string Color
        {
            get { return _color; }
            set
            {
                if (!Regex.IsMatch(value, @"\A\b[0-9a-fA-F]{6}\b\Z"))
                {
                    throw new ArgumentOutOfRangeException("value", "Color should be an hexadecimal string of length 6");
                }

                _color = value;
            }
        }

        /// <summary>
        /// A short description of the label (optional).
        /// </summary>
        public string Description { get; set; }

        internal string DebuggerDisplay
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "Name: {0}", Name);
            }
        }
    }
}