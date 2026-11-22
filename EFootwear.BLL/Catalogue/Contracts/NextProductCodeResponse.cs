namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>The code the next new product will get (docx 4.1: Product Id). It is only a preview; the real code is allocated when the product is saved.</summary>
public sealed record NextProductCodeResponse(string Code);
