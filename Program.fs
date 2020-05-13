// Learn more about F# at http://fsharp.org

open System
open b0wter.CouchDb.Lib
open b0wter.FSharp
open Newtonsoft.Json.Linq

let readUserName () =
    do printf "Please enter the user name for the database: "
    Console.ReadLine()


let readPassword () =
    do printf "Please enter the password for the database: "
    Console.ReadLine()


let readCredentials () =
    let user = readUserName ()
    let password = readPassword ()
    match Credentials.create(user, password) |> Credentials.validate with
    | Credentials.CredentialStatus.Valid c -> Ok c
    | Credentials.CredentialStatus.MissingUsername -> Error "Username is empty."
    | Credentials.CredentialStatus.MissingPassword -> Error "Password is empty."
    | Credentials.CredentialStatus.MissingUsernameAndPassword -> Error "Username and password are empty."


let createDbProperties host port database credentials =
    let props = DbProperties.create(host, port, credentials, DbProperties.ConnectionType.Http)
    match props with
    | DbProperties.DbPropertiesCreateResult.Valid properties -> Ok properties
    | DbProperties.DbPropertiesCreateResult.HostIsEmpty -> Error "Host name is emty."
    | DbProperties.DbPropertiesCreateResult.PortIsInvalid -> Error "Invalid port"


let authenticate props =
    async {
        let! result = Server.Authenticate.query props
        return match result with
               | Server.Authenticate.Result.Success _ -> Ok ()
               | Server.Authenticate.Result.Found _ -> Ok ()
               | Server.Authenticate.Result.Unauthorized _ -> Error "Unknown username/password"
               | Server.Authenticate.Result.JsonDeserialisationError _ -> Error "JsonDeserialization of the servers response failed."
               | Server.Authenticate.Result.Unknown x -> Error <| sprintf "Unkown error occured: %s" x.content
    }


let checkDatabaseExists props database =
    Databases.Exists.queryAsResult props database 
    |> AsyncResult.mapError (fun e -> e |> ErrorRequestResult.asString)
    |> Async.map (Result.bind (fun x -> if x then Ok () else Error "Database does not exist"))
    

let getDocuments props database (selector: Mango.Expression) =
    async {
        do printfn "%s" (Newtonsoft.Json.JsonConvert.SerializeObject(selector))
        let! result = Databases.Find.queryObjectsAsResult props database selector
        return result |> Result.mapBoth (fun r -> r.docs) (fun e -> (e |> ErrorRequestResult.asString))
    }


(*
let addToDocument<'a> (path: string) (value: 'a) (j: JObject) =
    let rec run (parts: string list) (current: JProperty) =
        function
        | [ propertyName ] -> current.Add(JProperty(propertyName, value))
        | head :: tail -> if current.Type = JTokenType.Object
    let parts = path.Split(":")
    *)


let modifyDocument (migration: Migration.T) (j: JObject) =
    try
        do migration.ToRemove |> List.iter (fun toRemove -> j.SelectTokens(toRemove) |> Seq.iter (fun t -> t.Remove()))
        Ok j
    with
    | err -> Error err.Message
    

let modifyDocuments (migration: Migration.T) (docs: JObject list) : Result<JObject list, string> =
    docs 
    |> List.map (modifyDocument migration)
    |> Result.all


let saveDocuments (migration: Migration.T) props (docs: JObject list) =
    let foldResponse (response: Databases.BulkAdd.Response) =
        let formatError (f: Databases.BulkAdd.Failure) = sprintf "'%A' - %s: %s" f.id f.error f.reason
        let rec run (successes, failures) (results: Databases.BulkAdd.InsertResult list) =
            match results with
            | [] -> (successes, failures)
            | head :: tail -> match head with
                              | Databases.BulkAdd.InsertResult.Success s -> (s.id :: successes, failures)
                              | Databases.BulkAdd.InsertResult.Failure f -> (successes, (f |> formatError) :: failures)
        run ([], []) response
    Databases.BulkAdd.queryAsResult props migration.Database docs
    |> AsyncResult.map (fun response -> response |> foldResponse)
    |> AsyncResult.mapError (fun error -> error |> ErrorRequestResult.asString)
    

let getDocumentsForMigration (m: Migration.T) props =
    let _checkDataBaseExists = fun (x: unit) -> checkDatabaseExists props m.Database
    let _getDocuments = fun (x: unit) -> getDocuments props m.Database m.Selector
    authenticate props |> AsyncResult.bindA _checkDataBaseExists |> AsyncResult.bindA _getDocuments |> AsyncResult.bind (modifyDocuments m)


let migrate (m: Migration.T)=
    async {
        let credentials = readCredentials ()
        let props = credentials |> Result.bind (createDbProperties m.Host m.Port m.Database)

        match props with
        | Ok p ->
            let _getDocuments = getDocumentsForMigration m
            let _modifyDocuments = modifyDocuments m
            let _saveDocuments = saveDocuments m p

            return! _getDocuments p |> AsyncResult.bind _modifyDocuments |> AsyncResult.bindA _saveDocuments
        | Error e -> return Error e 
    }
        

let printSuccessResult (successes: Guid list, failures: string list) =
    do printfn "Successful migrated the following documents:"
    do if successes.IsEmpty then printfn "No successful migration." else successes |> List.iter (printfn "%A")
    do printfn ""
    do printfn "Unsuccessful migrations:"
    do if failures.IsEmpty then printfn "No unsuccessful migration." else failures |> List.iter (printfn "%s")


[<EntryPoint>]
let main argv =
    let migration = "migration.json" |> IO.File.ReadAllText |> Migration.deserialize 
    match Async.RunSynchronously (migration |> migrate) with
    | Ok o -> printSuccessResult o
    | Error e  -> printfn "Running the migration failed because, %s" e
    0 // return an integer exit code
