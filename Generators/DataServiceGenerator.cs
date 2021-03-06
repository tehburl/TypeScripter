﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace TypeScripter.Generators
{
	public static class DataServiceGenerator
	{
		public static List<string> Generate(string apiRelativePath, List<Type> apiControllers, HashSet<Type> models, string targetPath)
		{
			if (string.IsNullOrWhiteSpace(apiRelativePath))
			{
				return new List<string>();
			}
			const string methodTemplate = "\t\t{0}: ({1}): Observable<{2}> => this.http.{3}(`{4}`{5}).map((res: Response) => {6}).catch(this.handleError),";

			if (apiRelativePath.EndsWith("/"))
			{
				apiRelativePath = apiRelativePath.Substring(0, apiRelativePath.Length - 1);
			}

			var sb = new StringBuilder();

			sb.AppendLine("// tslint:disable:max-line-length");
			sb.AppendLine("// tslint:disable:member-ordering");
			sb.AppendLine("import { Injectable } from '@angular/core';");
			sb.AppendLine("import { Http, Response } from '@angular/http';");
			sb.AppendLine("import { Observable } from 'rxjs';");
			sb.AppendLine("import * as moment from 'moment';");
			foreach (var m in models.OrderBy(m => m.Name))
			{
				sb.AppendLine("import { " + m.Name + " } from '.';");
			}

			sb.AppendLine("");
			sb.AppendLine("@Injectable()");
			sb.AppendLine("export class DataService {");
			sb.AppendLine("\tconstructor(private http: Http) {}");
			sb.AppendLine();
			sb.AppendLine(string.Format("\tapiRelativePath: string = '{0}';", apiRelativePath));
			sb.AppendLine();

			int methodCount = 0;

			foreach (var apiController in apiControllers)
			{
				sb.AppendFormat("\t{0} = {{{1}", apiController.Name.Substring(0, apiController.Name.Length - "Controller".Length).Camelize(), Environment.NewLine);
				
				var methods = apiController.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
				methodCount += methods.Length;
				HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var method in methods.OrderBy(m => m.Name))
				{
					// TODO: don't try to generate overloaded methods, instead print out a warning message when that happens
					if (names.Contains(method.Name))
					{
						sb.AppendLine("ERROR! The controller '" + apiController.Name + "' has a duplicate method name: '" + method.Name + "'");
					}
					names.Add(method.Name);
					var parameters = method.GetParameters();
					var joinedParameters = string.Join(", ", parameters.Select(p => p.Name + ": " + p.ParameterType.ToTypeScriptType()));
					var url = CombineUri("${this.apiRelativePath}", apiController.Name.Substring(0, apiController.Name.Length - "Controller".Length), method.Name);
					var httpMethodName = GetHttpMethodName(method);
					if (httpMethodName == null) { continue; }
					if (httpMethodName != "post")
					{
						if (parameters.Length > 0)
						{
							url = url + "?" + string.Join("&", parameters.Select(GenerateGetString));
						}
						sb.AppendFormat(methodTemplate, method.Name.Camelize(), joinedParameters, method.ReturnType.ToTypeScriptType(), httpMethodName, url, "", GetResultMapperExpression(method.ReturnType));
						sb.AppendLine();
					}
					else
					{
						var allParameters = parameters;

						if (allParameters.Length > 1)
						{
							Console.WriteLine("/* WARNING! Only POST methods with a zero or one parameter are currently supported -- {0}.{1} */", method.DeclaringType, method.Name);
						}
						sb.AppendFormat(methodTemplate, 
							method.Name.Camelize(), 
							joinedParameters, 
							method.ReturnType.ToTypeScriptType(), 
							httpMethodName, 
							url, 
							allParameters.Length > 0 ? ", " + allParameters[0].Name : ", null", 
							GetResultMapperExpression(method.ReturnType));
						sb.AppendLine();
					}
				}
				sb.AppendLine("\t};");
			}

			sb.AppendLine();
			sb.AppendLine("\tprivate handleError(error: Response | any) {");
			sb.AppendLine("\t\tlet errMsg: string;");
			sb.AppendLine("\t\tif (error instanceof Response) {");
			sb.AppendLine("\t\t\tconst hdr = error.headers.get('ExceptionMessage');");
			sb.AppendLine("\t\t\tconst body = error.json() || '';");
			sb.AppendLine("\t\t\tconst err = body.error || JSON.stringify(body);");
			sb.AppendLine("\t\t\terrMsg = `${error.status}${hdr ? ' - ' + hdr : ''} - ${error.statusText || ''} ${err}`;");
			sb.AppendLine("\t\t} else {");
			sb.AppendLine("\t\t\terrMsg = error.message ? error.message : error.toString();");
			sb.AppendLine("\t\t}");
			sb.AppendLine("\t\tconsole.error(errMsg);");
			sb.AppendLine("\t\treturn Observable.throw(errMsg);");
			sb.AppendLine("\t}");
			sb.AppendLine("}");

			Utils.WriteIfChanged(sb.ToString(), Path.Combine(targetPath, "DataService.ts"));
			Console.WriteLine("Generated a data service with {0} controllers and {1} methods.", apiControllers.Count, methodCount);
			return new List<string> {"DataService"};
		}

		private static string GenerateGetString(ParameterInfo p)
		{
			if (p.ParameterType == typeof(DateTime))
			{
				return string.Format("{0}=${{{0}.toISOString()}}", p.Name);
			}
			return string.Format("{0}=${{{0}}}", p.Name);
		}

	    private static string CombineUri(params string[] parts) {
		    StringBuilder sb = new StringBuilder();
		    sb.Append(parts[0]);
		    for (int i = 1; i < parts.Length; i++) {
			    if (!parts[i - 1].EndsWith("/")) {
				    sb.Append('/');
			    }
			    sb.Append(parts[i].TrimStart('/'));
		    }
		    return sb.ToString();
	    }

		private static string GetResultMapperExpression(Type returnType)
		{
			if (typeof(IEnumerable<object>).IsAssignableFrom(returnType) && returnType.GetGenericArguments()[0].IsModelType())
			{
				return string.Format("res.json().map(r => new {0}(r))", returnType.GetGenericArguments()[0].Name);
			}
			if (returnType.IsModelType())
			{
				return string.Format("new {0}(res.json())", returnType.Name);
			}
			return "res.json()";
		}

		private static string GetHttpMethodName(MethodInfo method)
		{
			var attributes = method.GetCustomAttributes().Select(a => a.GetType().Name).ToList();
			foreach (var a in attributes)
			{
				if (a == "HttpGetAttribute") return "get";
				if (a == "HttpPutAttribute") return "put";
				if (a == "HttpPostAttribute") return "post";
				if (a == "HttpDeleteAttribute") return "delete";
			}

			if (method.Name.StartsWith("Get")) return "get";
			if (method.Name.StartsWith("Put")) return "put";
			if (method.Name.StartsWith("Post")) return "post";
			if (method.Name.StartsWith("Update")) return "post";
			if (method.Name.StartsWith("Delete")) return "delete";

			Console.WriteLine("WARNING:  The method '{0}.{1}' does not have a recognizable HTTP method.", method.DeclaringType.Name, method.Name);

			return null;
		}
	}
}
