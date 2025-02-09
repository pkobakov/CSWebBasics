﻿namespace SUS.MvcFramework.ViewEngine
{
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Emit;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Text.RegularExpressions;

    public class SusViewEngine : IViewEngine
    {
        public string GetHtml(string templateCode, object viewModel, string user)
        {
            string csharpCode = GenerateCSharpCodeFromTemplate(templateCode, viewModel);
            IView executableObject = GenerateExecutableCode(csharpCode, viewModel);
            string html = executableObject.ExecuteTemplate(viewModel, user);
            return html;
        }
        private string GenerateCSharpCodeFromTemplate(string templateCode, object viewModel)
        {
            string typeOfModel = "object";

            if (viewModel != null)
            {
                if (viewModel.GetType().IsGenericType)
                {
                    var modelName = viewModel.GetType().FullName;
                    var genericArguments = viewModel.GetType().GenericTypeArguments;
                    typeOfModel = modelName.Substring(0, modelName.IndexOf('`'))
                        + "<" + string.Join(",", genericArguments.Select(x => x.FullName)) + ">";
                }

                else
                {
                    typeOfModel = viewModel.GetType().FullName;
                }
            }

            string csharpCode = @"
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using SUS.MvcFramework.ViewEngine;

namespace ViewNamespace
{
   public class ViewClass : IView
   {
       public string ExecuteTemplate(object viewModel, string user)
       {
        var User = user;
        var Model = viewModel as " + typeOfModel + @";
        var html = new StringBuilder();

        " + GetMethodBody(templateCode) + @"

        return html.ToString();
       }
   }
}";

            return csharpCode;
        }

        private string GetMethodBody(string templateCode)
        {
            Regex csharpCodeRegex = new Regex(@"[^\""\s&\'\<]+");
            var supportedOperators = new List<string>
            { 
              "for",
              "foreach",
              "while",
              "if",
              "else"
            };
            StringBuilder csharpCode = new StringBuilder();
            StringReader sr = new StringReader(templateCode);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                if (supportedOperators.Any(x=>line.TrimStart().StartsWith("@" + x)))
                {
                    var atSignLocation = line.IndexOf("@");
                    line = line.Remove(atSignLocation, 1);
                    csharpCode.AppendLine(line); 
                }

                else if (line.TrimStart().StartsWith("{")||
                    line.TrimStart().StartsWith("}"))
                {
                    csharpCode.AppendLine(line);
                }

                else
                {
                    csharpCode.Append($"html.AppendLine(@\"");

                    while (line.Contains("@"))
                    {
                        var atSignLocation = line.IndexOf("@");
                        var htmlBeforeAtSign = line.Substring(0, atSignLocation);
                        csharpCode.Append(htmlBeforeAtSign.Replace("\"", "\"\"") + "\" + ");
                        var lineAfterAtSign = line.Substring(atSignLocation + 1);
                        var code = csharpCodeRegex.Match(lineAfterAtSign).Value;
                        csharpCode.Append(code + "+ @\"");
                        line = lineAfterAtSign.Substring(code.Length);
                        //csharpCode
                    }

                    csharpCode.AppendLine(line.Replace( "\"", "\"\"")+ "\");");
                }
            }

            return csharpCode.ToString();
        }

        private IView GenerateExecutableCode(string csharpCode, object viewModel)
        {
            var compileResult = CSharpCompilation.Create("ViewAssembly")
                                                  .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                                                  .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                                                  .AddReferences(MetadataReference.CreateFromFile(typeof(IView).Assembly.Location));

                                                  if (viewModel != null)
                                                  {
                                                      if (viewModel.GetType().IsGenericType)
                                                      {
                                                      var genericArgs = viewModel.GetType().GetGenericArguments();
                                                        foreach (var genericArg in genericArgs)
                                                        {
                                                          compileResult = compileResult
                                                                     .AddReferences(MetadataReference.CreateFromFile(genericArg.Assembly.Location));
                                                        }
                                                      }
                
                                                      compileResult = compileResult
                                                                      .AddReferences(MetadataReference.CreateFromFile(viewModel.GetType().Assembly.Location));
                                                  }

            var libraries = Assembly.Load( 
                                     new AssemblyName("netstandard")).GetReferencedAssemblies();

            foreach (var library in libraries)
            {
                compileResult = compileResult
                               .AddReferences(MetadataReference.CreateFromFile(
                                   Assembly.Load(library).Location));
            }

            compileResult = compileResult.AddSyntaxTrees(SyntaxFactory.ParseSyntaxTree(csharpCode));




            using (MemoryStream memoryStream = new MemoryStream())
            {
                EmitResult result = compileResult.Emit(memoryStream);
                if (!result.Success) 
                {
                  
                    return new ErrorView(result.Diagnostics
                                         .Where(e => e.Severity == DiagnosticSeverity.Error)
                                         .Select(e => e.GetMessage()),csharpCode);
                }
            
                try
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    var byteAssembly = memoryStream.ToArray();
                    var assembly = Assembly.Load(byteAssembly);
                    var viewType = assembly.GetType("ViewNamespace.ViewClass");
                    var instance = Activator.CreateInstance(viewType);
                    return instance as IView;
                }
            
                catch (Exception ex)
                {
            
                    return new ErrorView (new List<string> { ex.ToString() }, csharpCode );
                }
            }        
        }
    }
}
