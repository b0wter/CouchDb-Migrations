// Learn more about F# at http://fsharp.org

open System
open System.Linq
open b0wter.CouchDb.Lib
open b0wter.FSharp
open b0wter.FSharp.Collections
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
        do printfn "Using the following selector:"
        let! result = Databases.Find.queryJObjectsAsResultWithOutput props database selector
        let count = result |> Helpers.Result.get (fun r -> r.docs.Length) (fun _ -> 0)
        do printfn "Found %i matching documents." count
        return result |> Result.mapBoth (fun r -> r.docs) (fun e -> (e |> ErrorRequestResult.asString))
    }


let modifyDocuments (migration: Migration.Migration) (docs: JObject list) : Result<JObject list, string> =
    docs |> migration.ModifyDocuments


let saveDocuments (migration: Migration.Migration) props (docs: JObject list) =
    let foldResponse (response: Databases.BulkAdd.Response) =
        let formatError (f: Databases.BulkAdd.Failure) = sprintf "'%A' - %s: %s" f.id f.error f.reason
        let rec run (successes, failures) (results: Databases.BulkAdd.InsertResult list) =
            match results with
            | [] -> 
                (successes, failures)
            | head :: tail -> 
                match head with
                | Databases.BulkAdd.InsertResult.Success s -> run (s.id :: successes, failures) tail
                | Databases.BulkAdd.InsertResult.Failure f -> run (successes, (f |> formatError) :: failures) tail
        run ([], []) response
    Databases.BulkAdd.queryAsResult props migration.Database docs
    |> AsyncResult.map (fun response -> response |> foldResponse)
    |> AsyncResult.mapError (fun error -> error |> ErrorRequestResult.asString)
    

let printDocuments (migration: Migration.Migration) props (docs: JObject list) =
    let serialized = docs |> List.map (Core.serializeAsJson [])
    let print (s: string) =
        printfn "----- DOCUMENT START -----"
        printfn "%s" s

    do serialized |> List.iter print

    async {
        let ids = (docs |> List.map (fun d -> d.["_id"].ToString() |> Guid.Parse))
        return Ok (ids, [])
    }


let getDocumentsForMigration (m: Migration.Migration) props =
    let _checkDataBaseExists = fun (x: unit) -> checkDatabaseExists props m.Database
    let _getDocuments = fun (x: unit) -> getDocuments props m.Database (m.BuildSelector ())
    authenticate props |> AsyncResult.bindA _checkDataBaseExists |> AsyncResult.bindA _getDocuments |> AsyncResult.bind (modifyDocuments m)


let migrate (isDryRun: bool) (m: Migration.Migration) =
    async {
        let credentials = readCredentials ()
        let props = credentials |> Result.bind (createDbProperties m.Host m.Port m.Database)

        match props with
        | Ok p ->
            let _getDocuments = getDocumentsForMigration m
            let _modifyDocuments = modifyDocuments m
            let _outputDocuments = if isDryRun then (printDocuments m p) else (saveDocuments m p)

            return! _getDocuments p |> AsyncResult.bind _modifyDocuments |> AsyncResult.bindA _outputDocuments
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
    let isDryRun = argv |> Array.exists ((=) "-d")
    do printfn "You are running a migration %s" (if isDryRun then "as a dry run. Nothing will be sent to the database." else " on a database. This will most likely cause changed in the databse!")
    let migration = UserMigration.changeAllOfficersToActive ()
    match Async.RunSynchronously (migration |> migrate isDryRun) with
    | Ok o -> printSuccessResult o
    | Error e  -> printfn "Running the migration failed because, %s" e
    0 // return an integer exit code
