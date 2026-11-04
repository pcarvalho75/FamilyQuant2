using PatternTools.FastaTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FamilyQuant
{
    public class FastaItemExtended : FastaItem
    {
        public int USearchID { get; set; }
        private int userId;
        private bool hasUserId;

        // Keep the existing integer API for scientific/curation callers. JSON presence is
        // separate: a missing assignment must not silently become the real family zero.
        [System.Text.Json.Serialization.JsonIgnore]
        public int UserID { get => userId; set { userId = value; hasUserId = true; } }

        [System.Text.Json.Serialization.JsonInclude]
        [System.Text.Json.Serialization.JsonPropertyName("UserID")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        private int? StoredUserId
        {
            get => hasUserId ? userId : null;
            set { userId = value.GetValueOrDefault(); hasUserId = value.HasValue; }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        [System.ComponentModel.Browsable(false)]
        public bool HasUserId => hasUserId;
        public string UserAnnotation { get; set; } = string.Empty;

        /// <summary>The protein's Pfam domains by name, joined with <see cref="ProteinDomains.Separator"/>;
        /// empty until they are fetched, and for a protein too short for Pfam to call.</summary>
        public string Domains { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public Evidence.ProteinEvidence? Evidence { get; set; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWriting)]
        public double XIC { get; set; }

        public double XICUser { get; set; }

        /// <summary>The family's share from the project's own full-XIC quantitation (Compute / update
        /// full XIC), on the basis and shared-peptide rule chosen in Layer 3; null when none is computed.
        /// Derived on screen from the stored quant, so it is never written into the .fcf.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double? FullXicShare { get; set; }

    }
}
