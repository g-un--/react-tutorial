open System
open System.IO
open System.Net
open System.Text

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
#r @".\packages\suave\lib\net40\Suave.dll"
#r @".\packages\Newtonsoft.Json\lib\net40\Newtonsoft.Json.dll"

open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Writers
open Newtonsoft.Json
open Newtonsoft.Json.Serialization

type Comment = { Id: string; Author: string; Text: string; }
type Command = 
  | Add of Comment 
  | GetAll of AsyncReplyChannel<Comment list> 

let commentsAgent = MailboxProcessor<Command>.Start(fun inbox ->
  
  let rec messageLoop comments = async {
    let! msg = inbox.Receive()
    match msg with
    | Add(comment) -> return! messageLoop (comment::comments)
    | GetAll(replyChannel) -> 
        replyChannel.Reply(comments)
        return! messageLoop comments }

  messageLoop []) 

let jsonSettings = new JsonSerializerSettings()
jsonSettings.ContractResolver <- new CamelCasePropertyNamesContractResolver() 

let getComments context = async {
  let! comments = commentsAgent.PostAndAsyncReply GetAll
  let json = JsonConvert.SerializeObject(comments, jsonSettings)
  return! OK json context 
} 

let postComment context = async {
  let body = Encoding.UTF8.GetString(context.request.rawForm)
  let comment = JsonConvert.DeserializeObject<Comment>(body, jsonSettings)
  Add comment |> commentsAgent.Post
  return! getComments context 
}

let app = 
  choose [
    path "/" >=> GET >=> Files.file "index.html"
    path "/scripts/tutorial.js" >=> GET >=> Files.file @".\scripts\tutorial.js"
    path "/api/comments" >=> choose [ 
      GET >=> Writers.setMimeType "application/json; charset=utf-8" >=> warbler (fun context -> getComments)
      POST >=> Writers.setMimeType "application/json; charset=utf-8" >=> warbler(fun context -> postComment) ] ]
  
startWebServer defaultConfig app 
