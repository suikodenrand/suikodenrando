#:property PublishAot=false
using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

// Read the published assembly's metadata without loading or running launcher code.
if (args.Length is < 1 or > 2)
    throw new ArgumentException("Expected the reviewed ATB SHA-256 and optionally the published launcher core path.");
using var source = args.Length == 2 ? File.OpenRead(args[1]) : Console.OpenStandardInput();
using var image = new MemoryStream();
source.CopyTo(image);
image.Position = 0;
using var pe = new PEReader(image);
var md = pe.GetMetadataReader();
var assembly = md.GetAssemblyDefinition();
if (md.GetString(assembly.Name) != "StarsOfDestiny.Launcher.Core")
    throw new InvalidDataException("Expected the published StarsOfDestiny.Launcher.Core assembly.");

string? hash = null;
foreach (var handle in assembly.GetCustomAttributes())
{
    var attribute = md.GetCustomAttribute(handle);
    if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
    var ctor = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
    if (ctor.Parent.Kind != HandleKind.TypeReference) continue;
    var type = md.GetTypeReference((TypeReferenceHandle)ctor.Parent);
    if (md.GetString(type.Namespace) != "System.Reflection" || md.GetString(type.Name) != "AssemblyMetadataAttribute") continue;
    var value = md.GetBlobReader(attribute.Value);
    if (value.ReadUInt16() != 1) throw new InvalidDataException("Invalid assembly metadata.");
    if (value.ReadSerializedString() != "StarsOfDestiny.ReviewedAtbSha256") continue;
    if (hash != null) throw new InvalidDataException("Duplicate ATB hash in launcher.");
    hash = value.ReadSerializedString() ?? throw new InvalidDataException("Missing ATB hash in launcher.");
}
if (hash == null)
{
    var installation = md.TypeDefinitions.Select(md.GetTypeDefinition).Single(type =>
        md.GetString(type.Namespace) == "StarsOfDestiny.Launcher.Core" && md.GetString(type.Name) == "GameInstallation");
    var field = installation.GetFields().Select(md.GetFieldDefinition).Single(f => md.GetString(f.Name) == "ReviewedAtbHash");
    var constant = md.GetConstant(field.GetDefaultValue());
    if (constant.TypeCode != ConstantTypeCode.String) throw new InvalidDataException("Missing default ATB hash.");
    hash = Encoding.Unicode.GetString(md.GetBlobBytes(constant.Value));
}
if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || !hash.Equals(args[0], StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Published launcher guard does not match the reviewed ATB DLL.");
