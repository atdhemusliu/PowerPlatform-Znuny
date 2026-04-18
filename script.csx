public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (this.Context.OperationId)
        {
            case "CreateTicket":
                return await ProcessCreateTicket().ConfigureAwait(false);
            case "UpdateTicket":
                return await ProcessUpdateTicket().ConfigureAwait(false);
            case "GetTicket":
                return await ProcessGetTicket().ConfigureAwait(false);
            case "SearchTickets":
                return await ProcessSearchTickets().ConfigureAwait(false);
            case "AddArticle":
                return await ProcessAddArticle().ConfigureAwait(false);
            default:
                var bad = new HttpResponseMessage(HttpStatusCode.BadRequest);
                bad.Content = CreateJsonContent(JsonConvert.SerializeObject(new
                {
                    error = new { code = "UnknownOperation", message = $"Unknown operation ID '{this.Context.OperationId}'" }
                }));
                return bad;
        }
    }

    private async Task<HttpResponseMessage> ProcessCreateTicket()
    {
        var connErr = ValidateAgentConnection();
        if (connErr != null) return connErr;

        var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var req = JsonConvert.DeserializeObject<CreateTicketRequest>(raw);
        if (req == null)
            return ErrorResponse(HttpStatusCode.BadRequest, "InvalidBody", "Could not parse CreateTicket JSON body.");

        var body = new JObject();
        InjectAuth(body);

        var ticket = new JObject
        {
            ["Title"] = req.Title,
            ["QueueID"] = req.QueueID,
            ["State"] = req.State,
            ["PriorityID"] = req.PriorityID,
            ["CustomerUser"] = req.CustomerUser
        };
        AddIfHasValue(ticket, "Type", req.Type);
        AddIfHasValue(ticket, "ServiceID", req.ServiceID);
        body["Ticket"] = ticket;

        var subject = string.IsNullOrWhiteSpace(req.ArticleSubject) ? req.Title : req.ArticleSubject;
        var channel = string.IsNullOrWhiteSpace(req.ArticleCommunicationChannel) ? "Internal" : req.ArticleCommunicationChannel;
        var contentType = string.IsNullOrWhiteSpace(req.ArticleContentType) ? "text/plain; charset=utf8" : req.ArticleContentType;
        var article = new JObject
        {
            ["Subject"] = subject,
            ["Body"] = req.ArticleBody,
            ["CommunicationChannel"] = channel,
            ["ContentType"] = contentType
        };
        var att = BuildOptionalAttachment(req.AttachmentFilename, req.AttachmentContentType, req.AttachmentContentBase64);
        if (att != null) article["Attachment"] = att;
        body["Article"] = article;

        AppendDynamicFields(body, req.DynamicFields);

        RewriteRequestUri("TicketCreate");
        this.Context.Request.Content = CreateJsonContent(body.ToString(Formatting.None));
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        return await NormalizeResponseAsync(response, ResponseTransform.CreateTicket).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ProcessUpdateTicket()
    {
        var connErr = ValidateAgentConnection();
        if (connErr != null) return connErr;

        var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var req = JsonConvert.DeserializeObject<UpdateTicketRequest>(raw);
        if (req == null || string.IsNullOrWhiteSpace(req.TicketID))
            return ErrorResponse(HttpStatusCode.BadRequest, "InvalidBody", "TicketID is required.");

        var body = new JObject();
        InjectAuth(body);
        body["TicketID"] = req.TicketID;

        var ticket = new JObject();
        AddIfHasValue(ticket, "Title", req.Title);
        AddIfHasValue(ticket, "QueueID", req.QueueID);
        AddIfHasValue(ticket, "State", req.State);
        AddIfHasValue(ticket, "PriorityID", req.PriorityID);
        AddIfHasValue(ticket, "Type", req.Type);
        AddIfHasValue(ticket, "ServiceID", req.ServiceID);
        AddIfHasValue(ticket, "CustomerUser", req.CustomerUser);
        if (ticket.HasValues) body["Ticket"] = ticket;

        AppendDynamicFields(body, req.DynamicFields);

        RewriteRequestUri("TicketUpdate");
        this.Context.Request.Content = CreateJsonContent(body.ToString(Formatting.None));
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        return await NormalizeResponseAsync(response, ResponseTransform.TicketMutation).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ProcessGetTicket()
    {
        var connErr = ValidateAgentConnection();
        if (connErr != null) return connErr;

        var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var req = JsonConvert.DeserializeObject<GetTicketRequest>(raw);
        if (req == null || string.IsNullOrWhiteSpace(req.TicketID))
            return ErrorResponse(HttpStatusCode.BadRequest, "InvalidBody", "TicketID is required.");

        var body = new JObject();
        InjectAuth(body);
        body["TicketID"] = req.TicketID;
        if (req.IncludeDynamicFields == true) body["DynamicFields"] = "1";
        if (req.IncludeAllArticles == true) body["AllArticles"] = "1";
        if (req.IncludeAttachments == true) body["Attachments"] = "1";

        RewriteRequestUri("TicketGet");
        this.Context.Request.Content = CreateJsonContent(body.ToString(Formatting.None));
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        return await NormalizeResponseAsync(response, ResponseTransform.Passthrough).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ProcessSearchTickets()
    {
        var connErr = ValidateAgentConnection();
        if (connErr != null) return connErr;

        var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var req = JsonConvert.DeserializeObject<SearchTicketsRequest>(raw);
        if (req == null) req = new SearchTicketsRequest();

        var body = new JObject();
        InjectAuth(body);
        AddIfHasValue(body, "ServiceID", req.ServiceID);
        AddIfHasValue(body, "Title", req.Title);

        if (!string.IsNullOrWhiteSpace(req.States))
        {
            var arr = new JArray();
            foreach (var part in req.States.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim();
                if (t.Length > 0) arr.Add(t);
            }
            if (arr.Count > 0) body["States"] = arr;
        }

        if (req.ExtraFilters != null)
        {
            foreach (var p in req.ExtraFilters.Properties())
            {
                body[p.Name] = p.Value;
            }
        }

        RewriteRequestUri("TicketSearch");
        this.Context.Request.Content = CreateJsonContent(body.ToString(Formatting.None));
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        return await NormalizeResponseAsync(response, ResponseTransform.SearchTickets).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ProcessAddArticle()
    {
        var connErr = ValidateAgentConnection();
        if (connErr != null) return connErr;

        var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var req = JsonConvert.DeserializeObject<AddArticleRequest>(raw);
        if (req == null || string.IsNullOrWhiteSpace(req.TicketID))
            return ErrorResponse(HttpStatusCode.BadRequest, "InvalidBody", "TicketID is required.");

        var body = new JObject();
        InjectAuth(body);
        body["TicketID"] = req.TicketID;

        var channel = string.IsNullOrWhiteSpace(req.CommunicationChannel) ? "Internal" : req.CommunicationChannel;
        var contentType = string.IsNullOrWhiteSpace(req.ContentType) ? "text/plain; charset=utf8" : req.ContentType;
        var article = new JObject
        {
            ["Subject"] = req.Subject,
            ["Body"] = req.Body,
            ["CommunicationChannel"] = channel,
            ["ContentType"] = contentType
        };
        var att = BuildOptionalAttachment(req.AttachmentFilename, req.AttachmentContentType, req.AttachmentContentBase64);
        if (att != null) article["Attachment"] = att;
        body["Article"] = article;

        RewriteRequestUri("TicketUpdate");
        this.Context.Request.Content = CreateJsonContent(body.ToString(Formatting.None));
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        return await NormalizeResponseAsync(response, ResponseTransform.AddArticle).ConfigureAwait(false);
    }

    private enum ResponseTransform
    {
        Passthrough,
        CreateTicket,
        TicketMutation,
        SearchTickets,
        AddArticle
    }

    private HttpResponseMessage ValidateAgentConnection()
    {
        if (string.IsNullOrWhiteSpace(GetConnectionParameter("agentUserLogin")) ||
            string.IsNullOrWhiteSpace(GetConnectionParameter("agentPassword")))
        {
            return ErrorResponse(HttpStatusCode.BadRequest, "ConnectionConfiguration",
                "Connection must include agentUserLogin and agentPassword.");
        }
        return null;
    }

    private void InjectAuth(JObject body)
    {
        body["UserLogin"] = GetConnectionParameter("agentUserLogin");
        body["Password"] = GetConnectionParameter("agentPassword");
    }

    private string GetConnectionParameter(string key)
    {
        try
        {
            dynamic ctx = this.Context;
            object v = ctx.ConnectionParameters[key];
            if (v != null) return v.ToString();
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogInformation($"GetConnectionParameter {key}: {ex.Message}");
        }
        return null;
    }

    private void RewriteRequestUri(string otrsOperation)
    {
        var current = this.Context.Request.RequestUri;
        var root = current.GetLeftPart(UriPartial.Authority);
        var ws = SanitizeWebServiceName(GetConnectionParameter("webServiceName"));
        var path = "/otrs/nph-genericinterface.pl/Webservice/" + ws + "/" + otrsOperation;
        this.Context.Request.RequestUri = new Uri(root + path);
    }

    private static string SanitizeWebServiceName(string ws)
    {
        if (string.IsNullOrWhiteSpace(ws)) return "RESTConnector";
        foreach (var c in ws.Trim())
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                return "RESTConnector";
        }
        return ws.Trim();
    }

    private static void AddIfHasValue(JObject obj, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) obj[name] = value;
    }

    private static JObject BuildOptionalAttachment(string filename, string contentType, string contentBase64)
    {
        if (string.IsNullOrWhiteSpace(filename) || string.IsNullOrWhiteSpace(contentType) || string.IsNullOrWhiteSpace(contentBase64))
            return null;
        return new JObject
        {
            ["Filename"] = filename,
            ["ContentType"] = contentType,
            ["Content"] = EnsureBase64(contentBase64)
        };
    }

    private static string EnsureBase64(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var trimmed = content.Trim();
        try
        {
            Convert.FromBase64String(trimmed);
            return trimmed;
        }
        catch
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        }
    }

    private static void AppendDynamicFields(JObject body, System.Collections.Generic.List<DynamicFieldItem> fields)
    {
        if (fields == null || fields.Count == 0) return;
        var arr = new JArray();
        foreach (var f in fields)
        {
            if (f == null || string.IsNullOrWhiteSpace(f.Name)) continue;
            arr.Add(new JObject { ["Name"] = f.Name, ["Value"] = f.Value ?? string.Empty });
        }
        if (arr.Count > 0) body["DynamicField"] = arr;
    }

    private HttpResponseMessage ErrorResponse(HttpStatusCode status, string code, string message)
    {
        var r = new HttpResponseMessage(status);
        r.Content = CreateJsonContent(JsonConvert.SerializeObject(new { error = new { code, message } }));
        return r;
    }

    private async Task<HttpResponseMessage> NormalizeResponseAsync(HttpResponseMessage response, ResponseTransform transform)
    {
        var text = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
        {
            return CloneStatus(response, text);
        }

        JObject jobj;
        try
        {
            jobj = JObject.Parse(text);
        }
        catch
        {
            return CloneStatus(response, text);
        }

        var logicalError = TryGetOtrsLogicalError(jobj);
        if (logicalError != null)
        {
            var status = MapOtrsErrorToHttp(logicalError.Item1);
            return BuildErrorResponse(status, logicalError.Item1, logicalError.Item2, jobj);
        }

        if (!response.IsSuccessStatusCode)
        {
            return CloneStatus(response, text);
        }

        switch (transform)
        {
            case ResponseTransform.SearchTickets:
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = CreateJsonContent(FormatSearchResponse(jobj).ToString(Formatting.None))
                };
            case ResponseTransform.CreateTicket:
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = CreateJsonContent(FormatCreateTicketResponse(jobj).ToString(Formatting.None))
                };
            case ResponseTransform.TicketMutation:
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = CreateJsonContent(FormatTicketMutationResponse(jobj).ToString(Formatting.None))
                };
            case ResponseTransform.AddArticle:
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = CreateJsonContent(FormatAddArticleResponse(jobj).ToString(Formatting.None))
                };
            default:
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = CreateJsonContent(jobj.ToString(Formatting.None))
                };
        }
    }

    private static HttpResponseMessage CloneStatus(HttpResponseMessage response, string text)
    {
        var r = new HttpResponseMessage(response.StatusCode);
        foreach (var h in response.Headers) r.Headers.TryAddWithoutValidation(h.Key, h.Value);
        r.Content = new StringContent(text ?? string.Empty, Encoding.UTF8, "application/json");
        return r;
    }

    private static Tuple<string, string> TryGetOtrsLogicalError(JObject jobj)
    {
        var errTok = jobj["Error"];
        if (errTok is JObject errObj)
        {
            var code = errObj["ErrorCode"]?.ToString() ?? "OTRS.Error";
            var msg = errObj["ErrorMessage"]?.ToString() ?? "Request failed.";
            return Tuple.Create(code, msg);
        }

        var legacyMsg = jobj["Error Message"]?.ToString();
        if (!string.IsNullOrWhiteSpace(legacyMsg))
        {
            return Tuple.Create("OTRS.LegacyError", legacyMsg);
        }

        return null;
    }

    private static HttpStatusCode MapOtrsErrorToHttp(string errorCode)
    {
        if (string.IsNullOrEmpty(errorCode)) return HttpStatusCode.BadRequest;
        var c = errorCode.ToUpperInvariant();
        if (c.Contains("AUTH") || c.Contains("LOGIN") || c.Contains("PASSWORD") || c.Contains("ACCESS") || c.Contains("DENIED"))
            return HttpStatusCode.Unauthorized;
        return HttpStatusCode.BadRequest;
    }

    private static HttpResponseMessage BuildErrorResponse(HttpStatusCode status, string code, string message, JObject raw)
    {
        var r = new HttpResponseMessage(status);
        var payload = new JObject
        {
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message,
                ["raw"] = raw
            }
        };
        r.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
        return r;
    }

    private static JObject FormatSearchResponse(JObject jobj)
    {
        var outObj = new JObject();
        var ids = new JArray();
        var tok = jobj["TicketID"];
        if (tok is JArray ja)
        {
            foreach (var x in ja) ids.Add(x);
        }
        else if (tok != null && tok.Type != JTokenType.Null)
        {
            ids.Add(tok.ToString());
        }
        outObj["TicketIDs"] = ids;
        return outObj;
    }

    private static JObject FormatCreateTicketResponse(JObject jobj)
    {
        var o = new JObject();
        CopyIfPresent(jobj, o, "TicketID");
        CopyIfPresent(jobj, o, "TicketNumber");
        CopyIfPresent(jobj, o, "ArticleID");
        return o;
    }

    private static JObject FormatTicketMutationResponse(JObject jobj)
    {
        var o = new JObject();
        CopyIfPresent(jobj, o, "TicketID");
        CopyIfPresent(jobj, o, "TicketNumber");
        return o;
    }

    private static JObject FormatAddArticleResponse(JObject jobj)
    {
        var o = new JObject();
        CopyIfPresent(jobj, o, "TicketID");
        CopyIfPresent(jobj, o, "ArticleID");
        return o;
    }

    private static void CopyIfPresent(JObject src, JObject dst, string name)
    {
        var t = src[name];
        if (t != null && t.Type != JTokenType.Null) dst[name] = t;
    }
}

public class DynamicFieldItem
{
    public string Name { get; set; }
    public string Value { get; set; }
}

public class CreateTicketRequest
{
    public string Title { get; set; }
    public string QueueID { get; set; }
    public string State { get; set; }
    public string PriorityID { get; set; }
    public string CustomerUser { get; set; }
    public string Type { get; set; }
    public string ServiceID { get; set; }
    public string ArticleSubject { get; set; }
    public string ArticleBody { get; set; }
    public string ArticleCommunicationChannel { get; set; }
    public string ArticleContentType { get; set; }
    public string AttachmentFilename { get; set; }
    public string AttachmentContentType { get; set; }
    public string AttachmentContentBase64 { get; set; }
    public System.Collections.Generic.List<DynamicFieldItem> DynamicFields { get; set; }
}

public class UpdateTicketRequest
{
    public string TicketID { get; set; }
    public string Title { get; set; }
    public string QueueID { get; set; }
    public string State { get; set; }
    public string PriorityID { get; set; }
    public string Type { get; set; }
    public string ServiceID { get; set; }
    public string CustomerUser { get; set; }
    public System.Collections.Generic.List<DynamicFieldItem> DynamicFields { get; set; }
}

public class GetTicketRequest
{
    public string TicketID { get; set; }
    public bool? IncludeDynamicFields { get; set; }
    public bool? IncludeAllArticles { get; set; }
    public bool? IncludeAttachments { get; set; }
}

public class SearchTicketsRequest
{
    public string ServiceID { get; set; }
    public string States { get; set; }
    public string Title { get; set; }
    public JObject ExtraFilters { get; set; }
}

public class AddArticleRequest
{
    public string TicketID { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
    public string CommunicationChannel { get; set; }
    public string ContentType { get; set; }
    public string AttachmentFilename { get; set; }
    public string AttachmentContentType { get; set; }
    public string AttachmentContentBase64 { get; set; }
}
