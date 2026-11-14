// Independent CLR shapes exercise the production reflection resolver. The good
// owner name/signature comes from a real PropertyDefinition row, not getter text.
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using SuikodenRandomizer.Suikoden2Plugin;

internal static partial class Program
{
    private enum IteratorShape
    {
        Exact, FormerThreeUnderscores, MissingOwner, StaticOwner, WrongOwnerType,
        IndexedOwner, SetterOnlyOwner, InheritedOwner, AmbiguousOwner,
        StaticMoveNext, WrongMoveNextResult, MoveNextNeedsArgument,
        InheritedMoveNext, AmbiguousMoveNext, SpoofBooleanResult
    }
    private static (Type Parent,Type Iterator) IteratorFixture(IteratorShape shape)
    {
        var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("IndependentIterator."+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.RunAndCollect);
        var module=assembly.DefineDynamicModule("Shape");
        var parent=module.DefineType("IteratorOwner",TypeAttributes.Public);
        TypeBuilder? baseType=shape is IteratorShape.InheritedOwner or IteratorShape.InheritedMoveNext
            ?module.DefineType("InheritedMembers",TypeAttributes.Public):null;
        var iterator=parent.DefineNestedType("_Load_d__19",TypeAttributes.NestedPublic,baseType);
        void Owner(TypeBuilder declaring,string name,Type result,bool indexed=false,bool isStatic=false,bool setterOnly=false,string suffix="")
        {
            Type[] parameters=indexed?new[]{typeof(int)}:Type.EmptyTypes;
            var property=declaring.DefineProperty(name,PropertyAttributes.None,result,parameters);
            if(setterOnly)
            {
                var setter=declaring.DefineMethod("set_"+name+suffix,MethodAttributes.Public|MethodAttributes.SpecialName|MethodAttributes.HideBySig,
                    typeof(void),new[]{result});setter.GetILGenerator().Emit(OpCodes.Ret);property.SetSetMethod(setter);return;
            }
            var getter=declaring.DefineMethod("get_"+name+suffix,MethodAttributes.Public|MethodAttributes.SpecialName|MethodAttributes.HideBySig
                |(isStatic?MethodAttributes.Static:0),result,parameters);
            var il=getter.GetILGenerator();il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Ret);property.SetGetMethod(getter);
        }
        if(shape!=IteratorShape.MissingOwner)
        {
            var declaring=shape==IteratorShape.InheritedOwner?baseType!:iterator;
            Owner(declaring,shape==IteratorShape.FormerThreeUnderscores?"___4__this":"__4__this",
                shape==IteratorShape.WrongOwnerType?typeof(object):parent,
                indexed:shape==IteratorShape.IndexedOwner,isStatic:shape==IteratorShape.StaticOwner,setterOnly:shape==IteratorShape.SetterOnlyOwner);
            if(shape==IteratorShape.AmbiguousOwner)Owner(iterator,"__4__this",typeof(object),suffix:"_second");
        }
        Type? spoof=null;
        if(shape==IteratorShape.SpoofBooleanResult)spoof=module.DefineType("System.Boolean",TypeAttributes.Public).CreateType();
        void Move(TypeBuilder declaring,Type result,bool isStatic=false,bool needsArgument=false)
        {
            var method=declaring.DefineMethod("MoveNext",MethodAttributes.Public|MethodAttributes.HideBySig|(isStatic?MethodAttributes.Static:0),
                result,needsArgument?new[]{typeof(int)}:Type.EmptyTypes);
            var il=method.GetILGenerator();il.Emit(result==typeof(bool)||result==typeof(int)?OpCodes.Ldc_I4_0:OpCodes.Ldnull);il.Emit(OpCodes.Ret);
        }
        Move(shape==IteratorShape.InheritedMoveNext?baseType!:iterator,
            shape==IteratorShape.WrongMoveNextResult?typeof(int):spoof??typeof(bool),
            isStatic:shape==IteratorShape.StaticMoveNext,needsArgument:shape==IteratorShape.MoveNextNeedsArgument);
        if(shape==IteratorShape.AmbiguousMoveNext)Move(iterator,typeof(int));
        var ownerType=parent.CreateType()!;baseType?.CreateType();
        return (ownerType,iterator.CreateType()!);
    }
    static partial void IteratorResolverChecks()
    {
        Case("Iterator fixture owner contract is bound to actual installed PE Property row1700c2af",()=>{
            using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("SuikodenRandomizer.Suikoden2.Unites.Tests.expected-iterator-property.json")!;
            using var doc=JsonDocument.Parse(stream);var root=doc.RootElement;
            Check(root.GetProperty("sha256").GetString()=="8a63846ccc60be5e4255401d135d1f710b2b9349746f71b4cc54c7e6751d2d3d","Unexpected verified interop identity.");
            var iterator=root.GetProperty("types").EnumerateArray().Single();
            Check(iterator.GetProperty("name").GetString()==typeof(Unite.Unite41._Load_d__19).FullName
                &&iterator.GetProperty("token").GetString()=="02000a3b","Wrong exact nested TypeDef proof.");
            var properties=iterator.GetProperty("properties").EnumerateArray().ToArray();
            var row=properties.Single(p=>p.GetProperty("token").GetString()=="1700c2af");
            string propertyName=row.GetProperty("name").GetString()!;
            Check(propertyName=="__4__this"&&row.GetProperty("type").GetString()==typeof(Unite.Unite41).FullName
                &&row.GetProperty("parameters").GetArrayLength()==0,"Actual owner PropertyDefinition differs.");
            Check(properties.All(p=>p.GetProperty("name").GetString()!="___4__this"),"Former erroneous three-underscore property unexpectedly exists.");
            var getter=row.GetProperty("getter");var fake=typeof(Unite.Unite41._Load_d__19).GetProperty(propertyName,BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly)!;
            Check(fake is not null&&Gsd2UniteRawResourcesPatch.OwnerMemberName==propertyName
                &&fake.PropertyType==typeof(Unite.Unite41)&&fake.GetIndexParameters().Length==0,"Fake or production name diverged from Property row.");
            Check(!getter.GetProperty("isStatic").GetBoolean()&&getter.GetProperty("parameters").GetArrayLength()==0
                &&getter.GetProperty("returns").GetString()==fake!.GetMethod!.ReturnType.FullName
                &&getter.GetProperty("name").GetString()==fake.GetMethod.Name
                &&fake.GetMethod.IsPublic&&!fake.GetMethod.IsStatic&&fake.GetMethod.GetParameters().Length==0,
                "Getter metadata differs from independently verified row.");
            Check(typeof(Unite.Unite41._Load_d__19).GetProperty("___4__this") is null,"Fake still contains former typo.");
        });
        Case("Production pure resolver accepts an independently generated exact instance-property iterator",()=>{
            var shape=IteratorFixture(IteratorShape.Exact);
            var target=Gsd2UniteRawResourcesPatch.ResolveTarget(shape.Parent,shape.Iterator);
            Check(target.DeclaringType==shape.Iterator&&target.Name=="MoveNext"&&!target.IsStatic
                &&((MethodInfo)target).ReturnType==typeof(bool),"Exact supported CLR shape was not selected.");
        });
        foreach(var shape in Enum.GetValues<IteratorShape>().Where(s=>s!=IteratorShape.Exact))
            Case("Production pure resolver rejects unsupported "+shape+" without name/signature fallback",()=>{
                var types=IteratorFixture(shape);
                Throws(()=>Gsd2UniteRawResourcesPatch.ResolveTarget(types.Parent,types.Iterator));
            });
        Case("Production pure resolver rejects null or mismatched declaring parent types",()=>{
            var one=IteratorFixture(IteratorShape.Exact);var two=IteratorFixture(IteratorShape.Exact);
            Check(Throws(()=>Gsd2UniteRawResourcesPatch.ResolveTarget(null!,one.Iterator)) is ArgumentNullException,"Null parent accepted.");
            Check(Throws(()=>Gsd2UniteRawResourcesPatch.ResolveTarget(one.Parent,null!)) is ArgumentNullException,"Null iterator accepted.");
            Throws(()=>Gsd2UniteRawResourcesPatch.ResolveTarget(two.Parent,one.Iterator));
        });
    }
}

