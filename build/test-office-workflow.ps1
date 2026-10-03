param([string]$Exe = "$PSScriptRoot/../bin/Release/net48/KillerPDF.exe", [string]$Output = "$PSScriptRoot/../bin/OfficeValidation")
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run in Windows PowerShell with -STA.' }
$Exe = (Resolve-Path $Exe).Path
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path $Output | Out-Null
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$asm = [Reflection.Assembly]::LoadFrom($Exe)
[Windows.Application]::ResourceAssembly = $asm
$app = [Activator]::CreateInstance($asm.GetType('KillerPDF.App'))
$app.InitializeComponent()
$packages = Join-Path $PSScriptRoot '../bin/nuget-packages'
$refs = @($Exe,
    "$packages/pdfsharpcore/1.3.67/lib/netstandard2.0/PdfSharpCore.dll",
    "$packages/docnet.core/2.6.0/lib/netstandard2.0/Docnet.Core.dll",
    "$packages/microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8/Facades/netstandard.dll",
    [Windows.Application].Assembly.Location, [Windows.Media.Brush].Assembly.Location,
    [Windows.Point].Assembly.Location, [System.Xaml.XamlReader].Assembly.Location, [System.Linq.Enumerable].Assembly.Location)
$refs = @($refs | ForEach-Object { (Resolve-Path $_).Path })
$code = @'
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Docnet.Core;
using Docnet.Core.Models;
using KillerPDF;
public static class OfficeValidation {
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object obj, string name) { return obj.GetType().GetField(name, Flags).GetValue(obj); }
    static void Set(object obj, string name, object value) { obj.GetType().GetField(name, Flags).SetValue(obj, value); }
    static object Call(object obj, string name, params object[] args) {
        return obj.GetType().GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(obj, args);
    }
    static void Assert(bool result, string message) { if (!result) throw new Exception(message); }
    static PdfDictionary Dict(PdfItem item) { return item is PdfSharpCore.Pdf.Advanced.PdfReference ? (PdfDictionary)((PdfSharpCore.Pdf.Advanced.PdfReference)item).Value : (PdfDictionary)item; }
    static void Theme(string name) {
        var type = typeof(App).Assembly.GetType("KillerPDF.Services.ThemeManager");
        var accent=type.GetField("_lightAccent",BindingFlags.Static|BindingFlags.NonPublic);
        accent.SetValue(null,Enum.Parse(accent.FieldType,"Blue"));
        var method = type.GetMethod("ApplyInternal", BindingFlags.Static | BindingFlags.NonPublic);
        method.Invoke(null, new object[] {Enum.Parse(method.GetParameters()[0].ParameterType, name), false});
    }
    static void Locale() {
        var type = typeof(App).Assembly.GetType("KillerPDF.Services.LocaleManager");
        var field = type.GetField("_current", BindingFlags.Static | BindingFlags.NonPublic);
        var locale = Enum.Parse(field.FieldType, "ZhTW"); field.SetValue(null, locale);
        type.GetMethod("ApplyInternal", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[]{locale});
    }
    static void Png(BitmapSource bitmap, string file) {
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream = File.Create(file)) png.Save(stream);
    }
    static byte[] RenderPdf(string path, string image, bool flatten = false) {
        if(flatten) {
            using(var editable=PdfReader.Open(path,PdfDocumentOpenMode.Modify)) {
                typeof(MainWindow).GetMethod("FlattenWidgetAppearances",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{editable});
                path=image+".pdf";editable.Save(path);
            }
        }
        using(var doc = DocLib.Instance.GetDocReader(path, new PageDimensions(1.5)))
        using(var page = doc.GetPageReader(0)) {
            byte[] raw = page.GetImage(); int w=page.GetPageWidth(),h=page.GetPageHeight();
            int dark=0; for(int i=0;i<raw.Length;i+=4) {
                int a=raw[i+3]; for(int c=0;c<3;c++) raw[i+c]=(byte)((raw[i+c]*a+255*(255-a))/255);
                raw[i+3]=255; if(raw[i]<180) dark++;
            }
            Console.WriteLine("PDF ink pixels: "+dark); Assert(dark>200,"PDFium output is blank");
            Png(BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,raw,w*4),image);
            return raw;
        }
    }
    static void Visual(MainWindow window, string folder, int width, int height, double scale, string theme) {
        Theme(theme); Call(window,"RefreshOfficeToolbarLanguage");
        ((FrameworkElement)window.FindName("RootClipGrid")).Opacity=1;
        ((TextBlock)window.FindName("FileNameLabel")).Text="臺北市政府_申請表與補充說明_繁體中文長檔名_2026年10月_簽名寄回版本.pdf";
        Call(window,"ShowOfficeTask",width<800?3:1);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        var image = new RenderTargetBitmap((int)(width*scale),(int)(height*scale),96*scale,96*scale,PixelFormats.Pbgra32);
        image.Render(root); Png(image,Path.Combine(folder,"ui-"+theme+"-"+width+"x"+height+"-"+scale+".png"));
        var host=(FrameworkElement)window.FindName("OfficeToolbarHost");
        Assert(host.ActualWidth <= width, "Toolbar wider than viewport");
        if(width==620) foreach(ToolBar bar in (IEnumerable)Get(window,"_officeTaskBars")) if(bar.Visibility==Visibility.Visible) {
            var more=(FrameworkElement)bar.Template.FindName("OverflowButton",bar);
            Console.WriteLine("Overflow="+bar.HasOverflowItems+", bar="+bar.ActualWidth+", more="+more.Visibility+"/"+more.ActualWidth+"x"+more.ActualHeight+", opacity="+more.Opacity+", visible="+more.IsVisible+", pos="+more.TranslatePoint(new Point(),host));
            Assert(bar.HasOverflowItems && more.Visibility==Visibility.Visible && more.ActualWidth>=40 && more.TranslatePoint(new Point(),host).X+more.ActualWidth<=host.ActualWidth,"Narrow toolbar has inaccessible commands");
        }
    }
    public static void Run(string folder) {
        typeof(App).GetField("TempDir", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, folder);
        Locale(); Theme("Light");
        var window=new MainWindow();
        string source=Path.Combine(folder,"form-source.pdf");
        using(var doc=new PdfDocument()) {
            var page=doc.AddPage();page.Width=595;page.Height=842;
            var annots=new PdfArray(doc);page.Elements["/Annots"]=annots;
            var fields=new PdfArray(doc);
            for(int i=0;i<3;i++) {
                var widget=new PdfDictionary(doc);
                widget.Elements.SetName("/Type","/Annot");widget.Elements.SetName("/Subtype","/Widget");
                widget.Elements.SetName("/FT","/Tx");widget.Elements.SetString("/T","Field"+i);
                widget.Elements.SetString("/DA","/Helv 12 Tf 0 g");widget.Elements.SetInteger("/Ff",i==1?4096:0);
                widget.Elements.SetInteger("/Q",i==2?2:0);
                widget.Elements["/Rect"]=new PdfRectangle(new PdfSharpCore.Drawing.XRect(45,700-i*90,360,i==1?70:30));
                doc.Internals.AddObject(widget);annots.Elements.Add(widget.Reference);fields.Elements.Add(widget.Reference);
            }
            var acro=new PdfDictionary(doc);acro.Elements["/Fields"]=fields;doc.Internals.Catalog.Elements["/AcroForm"]=acro;
            doc.Save(source);
        }
        var live=PdfReader.Open(source,PdfDocumentOpenMode.Modify);
        Set(window,"_doc",live);Set(window,"_currentFile",source);Set(window,"_originalFile",source);
        var formFields=(IEnumerable)Call(window,"GetPageFormFields",0,595,842);
        var values=(Dictionary<int,string>)Get(window,"_formTextValues");
        string[] text={"  王小明  ","臺北市中正區\n忠孝西路一段100號","民國115/10/03"};int index=0;
        foreach(var field in formFields) {
            int key=(int)field.GetType().GetProperty("ObjNum").GetValue(field,null);
            values[key]=text[index++];
        }
        Assert(index==3,"Three editable fields not discovered");
        var dims=Get(window,"_renderDims");var add=dims.GetType().GetMethod("Add");
        add.Invoke(dims,new object[]{0,Activator.CreateInstance(add.GetParameters()[1].ParameterType,new object[]{595,842})});
        var annotations=(Dictionary<int,List<PageAnnotation>>)Get(window,"_annotations");
        annotations[0]=new List<PageAnnotation>{new TextAnnotation {PageIndex=0,Position=new Point(45,410),Content="申請人 王小明　2026/10/03 ✓",FontSize=18,Width=450,Height=40},
            new SignatureAnnotation {PageIndex=0,Position=new Point(70,490),Scale=1,SourceWidth=180,SourceHeight=70,
                Strokes=new List<List<Point>>{new List<Point>{new Point(0,45),new Point(40,10),new Point(30,60),new Point(130,20)}}}};
        Call(window,"MarkDirty",true);
        string probe=(string)Call(window,"CreateDocumentOutputSnapshot"); Console.WriteLine("Snapshot generated: "+probe);
        byte[] first=null;string target=Path.Combine(folder,"saved.pdf");
        for(int i=0;i<3;i++) {
            Assert((bool)Call(window,"SaveDocumentTo",target),"Save failed");
            Assert(Object.ReferenceEquals(Get(window,"_doc"),live),"Save replaced the active document");
            Assert(!(bool)Get(window,"_isDirty"),"Successful save left dirty");
            using(var saved=PdfReader.Open(target,PdfDocumentOpenMode.Modify)) {
                var anns=saved.Pages[0].Elements.GetArray("/Annots");
                for(int f=0;f<3;f++) {
                    var widget=Dict(anns.Elements[f]);
                    Assert(((PdfString)widget.Elements["/V"]).Value==text[f],"Unicode field value or whitespace lost: expected ["+text[f]+"], got ["+((PdfString)widget.Elements["/V"]).Value+"]");
                    Assert(widget.Elements.GetDictionary("/AP").Elements["/N"]!=null,"Widget appearance missing");
                    Assert(widget.Elements.GetName("/FT")=="/Tx","Field lost editability");
                }
            }
            byte[] rendered=RenderPdf(target,Path.Combine(folder,"saved-"+i+".png"), true);
            if(first==null) first=rendered;else Assert(first.SequenceEqual(rendered),"Repeated save changed pixels (duplicate annotations)");
        }
        Call(window,"MarkDirty",true);
        var before=Get(window,"_currentFile");int annotationCount=annotations[0].Count;
        string print=(string)Call(window,"CreateDocumentOutputSnapshot");
        Assert((bool)Get(window,"_isDirty") && Equals(before,Get(window,"_currentFile")),"Snapshot changed active state");
        Assert(first.SequenceEqual(RenderPdf(print,Path.Combine(folder,"print-snapshot.png"))),"Unsaved print snapshot differs from saved output");
        // Invalid pending image must fail output instead of silently dropping its content.
        annotations[0].Add(new SignatureAnnotation{PageIndex=0,ImageData="invalid-base64",SourceWidth=100,SourceHeight=50});
        bool failed=false;try { Call(window,"CreateDocumentOutputSnapshot"); } catch(TargetInvocationException) {failed=true;}
        Assert(failed && (bool)Get(window,"_isDirty") && annotations[0].Count==annotationCount+1,"Output failure lost edits or was ignored");
        annotations[0].RemoveAt(annotations[0].Count-1);
        // Selected checkbox without AP must fail, never disappear from output.
        using(var malformed=PdfReader.Open(source,PdfDocumentOpenMode.Modify)) {
            var widget=Dict(malformed.Pages[0].Elements.GetArray("/Annots").Elements[0]);
            widget.Elements.SetName("/FT","/Btn");widget.Elements.SetName("/V","/Yes");
            bool rejected=false;
            try { typeof(MainWindow).GetMethod("FlattenWidgetAppearances",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{malformed}); }
            catch(TargetInvocationException) {rejected=true;}
            Assert(rejected,"Selected checkbox without AP was silently omitted");
        }
        // Render the actual print controls and verify invalid/fullwidth ranges gate Print.
        var previewType=typeof(App).Assembly.GetType("KillerPDF.PrintPreviewWindow");
        var preview=(Window)Activator.CreateInstance(previewType,new object[]{null,1,new double[]{595.0*96/72},new double[]{842.0*96/72},print,null});
        var pageImage=BitmapFrame.Create(new Uri(Path.Combine(folder,"print-snapshot.png")),BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
        Call(preview,"SetRenderedPage",0,pageImage,pageImage.PixelWidth,pageImage.PixelHeight);Call(preview,"FinishLoading");
        var range=(TextBox)Get(preview,"_pagesBox");range.Text="１－１、１";
        Assert(((Button)Get(preview,"_printBtn")).IsEnabled,"Fullwidth print range rejected");
        range.Text="1-99";Assert(!((Button)Get(preview,"_printBtn")).IsEnabled,"Invalid print range enabled Print");
        var printRoot=(FrameworkElement)preview.Content;
        printRoot.Measure(new Size(900,680));printRoot.Arrange(new Rect(0,0,900,680));printRoot.UpdateLayout();
        var printImage=new RenderTargetBitmap(900,680,96,96,PixelFormats.Pbgra32);printImage.Render(printRoot);Png(printImage,Path.Combine(folder,"print-preview.png"));
        Call(window,"SaveTempAndReload",true,true);
        var reloaded=(IEnumerable)Call(window,"GetPageFormFields",0,595,842);index=0;
        foreach(var field in reloaded) {
            int key=(int)field.GetType().GetProperty("ObjNum").GetValue(field,null);
            Assert(values[key]==text[index++],"Form values lost after structural reload");
        }
        foreach(var theme in new[]{"Light","Dark"}) {
            Visual(window,folder,1366,768,1,theme);Visual(window,folder,1093,614,1.25,theme);
            Visual(window,folder,1280,720,1.5,theme);Visual(window,folder,960,540,2,theme);
            Visual(window,folder,620,480,1,theme);Visual(window,folder,1920,1080,1,theme);
        }
        File.WriteAllText(Path.Combine(folder,"result.txt"),"PASS: Unicode fields and appearance, editable widgets, whitespace, 3 identical repeated saves, pending text/signature output, active document preservation, unsaved print snapshot, strict failure preservation, checkbox AP failure, form reload, print range gating, 12 WPF layout renders and print preview.");
        Console.WriteLine(File.ReadAllText(Path.Combine(folder,"result.txt")));
    }
}
'@
$compiler = New-Object Microsoft.CSharp.CSharpCodeProvider
$options = New-Object System.CodeDom.Compiler.CompilerParameters
$options.GenerateInMemory = $true
$options.ReferencedAssemblies.AddRange([string[]]$refs)
$options.ReferencedAssemblies.Add('System.dll') | Out-Null
$result = $compiler.CompileAssemblyFromSource($options, $code)
if ($result.Errors.HasErrors) { throw ($result.Errors | Out-String) }
try { [OfficeValidation]::Run($Output) } catch { Write-Output $_.Exception.ToString(); throw }
