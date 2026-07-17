using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

var path2 = "E:\\Kodlarim\\Yeni\\src\\Integration.TradeXpress.Domain.Shared\\Localization\\TradeXpress\\en.json";
var json2 = File.ReadAllText(path2);
var doc2 = JsonNode.Parse(json2);
var texts2 = doc2["Texts"].AsObject();

texts2["Enum:ProcessDirectionType:Inbound:UI"] = "INBOUND";
texts2["Enum:ProcessDirectionType:Outbound:UI"] = "OUTBOUND";
texts2["Enum:ProcessPaymentType:Normal:UI"] = "NORMAL";
texts2["Enum:ProcessPaymentType:WithCash:UI"] = "PREPAID";
texts2["Enum:ProcessPaymentType:WithCurrency:UI"] = "WITH CURRENCY";
texts2["Enum:ProcessPaymentType:Return:UI"] = "RETURN";
texts2["Enum:ProcessPaymentType:Consignment:UI"] = "CONSIGNMENT";
texts2["Enum:ProcessPaymentType:Reservation:UI"] = "RESERVATION";

var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
File.WriteAllText(path2, doc2.ToJsonString(options));
