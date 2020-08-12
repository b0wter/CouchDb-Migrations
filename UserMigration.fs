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

    member this.RankMap = Map.empty
                             .Add("Zufü (Zugführer/-in)", SharedEntities.Models.LeadershipRole.PlatoonLeader)
                             .Add("Zutrufü (Zugtruppführer/-in)", SharedEntities.Models.LeadershipRole.PlatoonDeputy)
                             .Add("Grufü (Gruppenführer/-in)", SharedEntities.Models.LeadershipRole.SquadLeader)
                             .Add("stv. Gruppenführer/-in", SharedEntities.Models.LeadershipRole.SquadDeputy)

    member this.ModifyOfficerDoc (officer: JToken) =
        if officer.Type = JTokenType.Object then
            do printfn "%s" (officer.ToString())
            if (officer :?> JObject).ContainsKey("role") then 
                (officer :?> JObject).Property("role").Remove()
                do printfn "Removed role"
        else
            failwith "The given token is not an object!"

    member this.ModifyDocument (doc: JObject) =
        let squadToken = doc.GetValue("squads")
        match squadToken.Type with
        | JTokenType.Array -> 
            let array = squadToken :?> JArray
            let officers = array |> Seq.collect (fun a -> (a.Value<JArray>("officers"))) //:?> JArray)   
            do printfn "Found %i officers." (officers |> Seq.length)
            do officers |> Seq.iter this.ModifyOfficerDoc
            Ok doc
        | _ -> Error "Das Feld 'squads' ist kein Array."

    override this.ModifyDocuments docs =
        docs |> List.map this.ModifyDocument |> b0wter.FSharp.Result.all

let changeOfficerRole() : Migration =
    ChangeOfficerRoleMigration() :> Migration.Migration