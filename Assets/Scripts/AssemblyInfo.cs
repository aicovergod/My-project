using System.Runtime.CompilerServices;

/// <summary>
/// Grants Unity's editor test assembly access to internal members so edit mode tests
/// can exercise runtime components without loosening their public API surface.
/// </summary>
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
