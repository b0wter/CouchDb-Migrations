module Migration

open System
open b0wter.CouchDb.Lib
open Newtonsoft.Json
open Newtonsoft.Json.Linq

type NewField<'a> = {
    Key: string
    Value: 'a
}

type IntField = NewField<int>
type FloatField = NewField<float>
type StringField = NewField<string>
type BoolField = NewField<bool>

type Field
    = IntField of IntField
    | FloatField of FloatField
    | StringField of StringField
    | BoolField of BoolField

let intField key value      = { IntField.Key    = key; IntField.Value = value }     |> IntField
let floatField key value    = { FloatField.Key  = key; FloatField.Value = value }   |> FloatField
let stringField key value   = { StringField.Key = key; StringField.Value = value }  |> StringField
let boolField key value     = { BoolField.Key   = key; BoolField.Value = value }    |> BoolField

type T = {
    Database: string
    Host: string
    Port: int
    Selector: Mango.Expression
    ToAdd: Field list
    ToRemove: string list 
}

type NewFieldConverter() =
    inherit JsonConverter()
    
    override this.CanConvert(objectType) =
        let result = objectType.GetType().IsAssignableFrom(typeof<Field>) || objectType = typedefof<Field>
        result

    override this.WriteJson(_, _, _) =
        raise (System.NotImplementedException())

    override this.ReadJson(reader, objectType, existingValue, serializer) =
        let j = JObject.Load(reader)
        let key = j.GetValue("key", StringComparison.OrdinalIgnoreCase)
        let containsKey = key |> (not << isNull)
        let value = j.GetValue("value", StringComparison.OrdinalIgnoreCase)
        let containsValue = value |> (not << isNull)

        if containsKey && containsValue then
            let keyValue = key.Value<string>()
            match value.Type with
            | JTokenType.Integer -> (intField keyValue      (value.Value<int>()))
            | JTokenType.Float ->   (floatField keyValue    (value.Value<float>()))
            | JTokenType.String ->  (stringField keyValue   (value.Value<string>()))
            | JTokenType.Boolean -> (boolField keyValue     (value.Value<bool>()))
            | _ -> failwith <| sprintf "Unkown data type. The value property of the following is neither an int, a float, a string or a bool. It is of type '%s': %s" (value.Type.ToString()) (j.ToString())
            :> obj
        else
            let error = if containsKey && not containsValue then "the value field" else if not containsKey && containsValue then "the key field" else "the key and value fields"
            failwith <| sprintf "An entry in 'ToAdd' is missing %s." error

let deserialize (s: string) =
    let converters = [| MangoConverters.OperatorJsonConverter(true) :> JsonConverter; NewFieldConverter() :> JsonConverter |]
    JsonConvert.DeserializeObject<T>(s, converters)
