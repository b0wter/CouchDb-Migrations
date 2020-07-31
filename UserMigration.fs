module UserMigration

open System
open Migration
open b0wter.CouchDb.Lib.Mango
open Newtonsoft.Json.Linq

//
// Create your migration here.
//

type ChangeAssignmentTypeMigration() =
    inherit Migration()

    override this.Database = "stimpack"
    override this.Host = "localhost"
    override this.Port = 5984
    override this.Username = "admin"
    override this.Password = "password"
    override this.BuildSelector () =
        condition "type" (Equal <| Text "SpecialAssignment") |> createExpression

    member this.ModifyDocument (doc: JObject) =
        let key = "type"
        if doc.ContainsKey(key) then
            do doc.Property(key).Remove()
            do doc.Add(JProperty(key, "Absence"))
            Ok doc
        else
            Error (sprintf "The Object: '%s' does not contain the key '%s'." (doc.ToString()) key)

    override this.ModifyDocuments docs =
        docs |> List.map this.ModifyDocument |> b0wter.FSharp.Result.all //(fun d -> this.ModifyDocument())


let changeAssignmentType () : Migration =
    ChangeAssignmentTypeMigration() :> Migration.Migration

type ChangeOfficerRoleMigration() =
    inherit Migration()

    override this.Database = "stimpack"
    override this.Host = "localhost"
    override this.Port = 5984
    override this.Username = "admin"
    override this.Password = "password"
    override this.BuildSelector () =
        condition "type" (Equal <| Text "Platoon") |> createExpression

    member this.ModifyDocument (doc: JObject) =
        let squadToken = doc.GetValue("squads")
        match squadToken.Type with
        | JTokenType.Array -> 
            let array = squadToken :?> JArray
            let officers = array |> Seq.collect (fun a -> (a.Value<JArray>("officers"))) //:?> JArray)   
            Ok doc
        | _ -> Error "Das Feld 'squads' ist kein Array."

    override this.ModifyDocuments docs =
        docs |> List.map this.ModifyDocument |> b0wter.FSharp.Result.all

let changeOfficerRole() : Migration =
    ChangeAssignmentTypeMigration() :> Migration.Migration