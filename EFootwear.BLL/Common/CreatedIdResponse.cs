namespace EFootwear.BLL.Common;

/// <summary>The body of a 201 Created response: the id of the new record. JSON: <c>{ "id": "..." }</c>.</summary>
public sealed record CreatedIdResponse(Guid Id);
