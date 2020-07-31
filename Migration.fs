module Migration

open System
open b0wter.CouchDb.Lib
open Newtonsoft.Json
open Newtonsoft.Json.Linq

[<AbstractClass>]
type Migration() =
    abstract member Database: string
    abstract member Host: string
    abstract member Port: int
    abstract member Username: string
    abstract member Password: string
    abstract member BuildSelector: unit -> Mango.Expression
    abstract member ModifyDocuments: JObject list -> Result<JObject list, string>