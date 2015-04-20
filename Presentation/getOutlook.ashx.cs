﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
//using System.Web.UI;
//using System.Web.UI.WebControls;
using System.Web.Script.Serialization;
using Microsoft.Exchange.WebServices.Data;
using System.Net;
using DisplayMonkey.Language;

namespace DisplayMonkey
{
    public partial class getOutlook : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext context)
		{
			HttpRequest Request = context.Request;
			HttpResponse Response = context.Response;

            int frameId = DataAccess.IntOrZero(Request.QueryString["frame"]);
            int panelId = DataAccess.IntOrZero(Request.QueryString["panel"]);
            int displayId = DataAccess.IntOrZero(Request.QueryString["display"]);
            string culture = DataAccess.StringOrBlank(Request.QueryString["culture"]);

			string json = "";
				
			try
			{
                // set culture
                Outlook outlook = new Outlook(frameId);
                Location location = new Location(displayId);

                if (string.IsNullOrWhiteSpace(culture))
                    culture = location.Culture;
                
                if (!string.IsNullOrWhiteSpace(culture))
                {
                    System.Globalization.CultureInfo cultureInfo = new System.Globalization.CultureInfo(culture);
                    System.Threading.Thread.CurrentThread.CurrentCulture = cultureInfo;
                    System.Threading.Thread.CurrentThread.CurrentUICulture = cultureInfo;
                }

                // EWS: get data
                OutlookData data = HttpRuntime.Cache.GetOrAddSliding(
                    string.Format("outlook_{0}_{1}_{2}", location.LocationId, outlook.FrameId, outlook.Version),
                    () =>
                    {
                        return new OutlookData(outlook, location);
                    },
                    TimeSpan.FromMinutes(outlook.CacheInterval)
                    );

                // ---------------------- culture-specific starts here --------------------- //
                DateTime
                    locationTime = location.LocationTime,
                    locationToday = new DateTime(locationTime.Year, locationTime.Month, locationTime.Day)
                    ;

                List<EventEntry> currentList = data.events
                    .Where(e => e.Ends >= locationTime)
                    .Take(Math.Max(1, outlook.ShowEvents))
                    .ToList()
                    ;
                
                EventEntry 
                    currentEvent = null,
                    firstEvent = currentList.FirstOrDefault()
                    ;

                if (firstEvent != null && firstEvent.Starts <= locationTime)
                {
                    currentEvent = firstEvent;
                }

                string 
                    strCurrentEvent = "",
                    strCurrentStatus = string.Format(
                        Resources.Outlook_AvailableUntil,
                        new DateTime(data.endTime.Ticks).ToShortTimeString()
                        )
                    ;
                    
                if (currentEvent != null)
                {
                    DateTime tomorrow = locationToday.AddDays(1);
                    strCurrentEvent = string.Format("{0}, {1} - {2}", 
                        currentEvent.Subject,
                        (currentEvent.Starts >= tomorrow ? currentEvent.Starts.ToShortDateString() + " " : "") + currentEvent.Starts.ToShortTimeString(),
                        (currentEvent.Ends >= tomorrow ? currentEvent.Ends.ToShortDateString() + " " : "") + currentEvent.Ends.ToShortTimeString()
                        );
                    TimeSpan gap = currentEvent.Ends.Subtract(locationTime);
                    if (gap.Hours > 0)
                        strCurrentStatus = string.Format(Resources.Outlook_EndsInHrsMin, (int)gap.TotalHours, gap.Minutes);
                    else
                        strCurrentStatus = string.Format(Resources.Outlook_EndsInMin, gap.Minutes);
                }

                else if (firstEvent != null)
                {
                    TimeSpan gap = firstEvent.Starts.Subtract(locationTime);
                    if (gap.Hours > 0)
                        strCurrentStatus = string.Format(Resources.Outlook_AvailableForHrsMin, (int)gap.TotalHours, gap.Minutes);
                    else
                        strCurrentStatus = string.Format(Resources.Outlook_AvailableForMin, gap.Minutes);
                }
                    
                JavaScriptSerializer jss = new JavaScriptSerializer();
                jss.RegisterConverters(new[] { new EventEntryConverter() });
                json = jss.Serialize(new
                {
                    currentTime = new
                    {
                        year = locationTime.Year,
                        month = locationTime.Month,
                        day = locationTime.Day,
                        weekDay = locationTime.DayOfWeek,
                        hour = locationTime.Hour,
                        minute = locationTime.Minute,
                        second = locationTime.Second,
                        timeZone = new
                        {
                            id = data.timeZone.Id,
                            daylightName = data.timeZone.DaylightName,
                            utcOffsetHours = data.timeZone.BaseUtcOffset.TotalHours,
                        },
                    },
                    mailbox = data.DisplayName,
                    startTime = new
                    {
                        hour = data.startTime.Hours,
                        minute = data.startTime.Minutes,
                    },
                    endTime = new
                    {
                        hour = data.endTime.Hours,
                        minute = data.endTime.Minutes,
                    },
                    currentEvent = strCurrentEvent,
                    currentStatus = strCurrentStatus,
                    mode = outlook.Mode,
                    events = new
                    {
                        showEvents = outlook.ShowEvents,
                        head = EventEntryConverter.Head(),
                        items = currentList,
                        noEvents = currentList.Count == 0 ? Resources.Outlook_NoEventsToday : "",
                    },
                });
			}

			catch (Exception ex)
			{
                JavaScriptSerializer s = new JavaScriptSerializer();
                json = s.Serialize(new
                {
                    Error = ex.Message,
                    //Stack = ex.StackTrace,
                    Data = new
                    {
                        FrameId = frameId,
                        PanelId = panelId,
                        DisplayId = displayId,
                        Culture = culture
                    },
                });
			}

            Response.Clear();
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetSlidingExpiration(true);
            Response.Cache.SetNoStore();
            Response.ContentType = "application/json";
			Response.Write(json);
            Response.Flush();
        }

        #region -------- OutlookData --------

        private class OutlookData
        {
            public IEnumerable<EventEntry> events = null;
            public TimeSpan startTime = new TimeSpan(0, 0, 0);
            public TimeSpan endTime = new TimeSpan(23, 59, 59);
            public TimeZoneInfo timeZone = TimeZoneInfo.Local;
            public string DisplayName;

            public OutlookData(Outlook outlook, Location location)
            {
                OutlookData.initFromFrame(this, outlook, location);
            }

            #region -------- EWS Data Call --------

            private static void initFromFrame(OutlookData data, Outlook outlook, Location location)
            {
                DateTime
                    locationTime = location.LocationTime,
                    locationToday = new DateTime(locationTime.Year, locationTime.Month, locationTime.Day),
                    windowBeg = locationToday, //locationTime,
                    windowEnd = locationToday.AddDays(1).AddMilliseconds(-1)
                    ;

                // EWS: create connection point
                ServicePointManager.ServerCertificateValidationCallback = CertificateValidationCallBack;

                ExchangeService service = new ExchangeService(outlook.EwsVersion)
                {
                    Credentials = new WebCredentials(
                        outlook.Account,
                        RsaUtil.Decrypt(outlook.Password)
                        ),
                };

                // EWS: get URL
                if (!string.IsNullOrWhiteSpace(outlook.URL))
                {
                    service.Url = new Uri(outlook.URL);
                }
                else
                {
                    service.Url = HttpRuntime.Cache.GetOrAddSliding(
                        string.Format("exchange_account_{0}", outlook.Account),
                        () =>
                        {
                            service.AutodiscoverUrl(outlook.Account, RedirectionUrlValidationCallback);
                            return service.Url;
                        },
                        TimeSpan.FromHours(24)
                        );
                }

                // mailbox: get display name
                data.DisplayName = outlook.Name;
                if (string.IsNullOrWhiteSpace(data.DisplayName))
                {
                    var match = service.ResolveName(outlook.Mailbox);
                    if (match.Count > 0)
                    {
                        if (match[0].Contact != null)
                        {
                            data.DisplayName =
                                    match[0].Contact.CompleteName.FullName
                                ?? match[0].Contact.DisplayName
                                ?? data.DisplayName
                                ;
                        }

                        else if (match[0].Mailbox != null)
                        {
                            data.DisplayName =
                                match[0].Mailbox.Name
                                ?? data.DisplayName
                                ;
                        }
                    }
                    //else
                    //    throw new ApplicationException(string.Format("Mailbox {0} not found", mailbox));
                }

                // mailbox: get availability
                GetUserAvailabilityResults uars = service.GetUserAvailability(
                    new AttendeeInfo[] { new AttendeeInfo(outlook.Mailbox, MeetingAttendeeType.Required, true) },
                    new TimeWindow(locationToday, locationToday.AddDays(1)),
                    AvailabilityData.FreeBusy
                    );
                var u = uars.AttendeesAvailability[0];
                if (u.WorkingHours != null)
                {
                    data.startTime = u.WorkingHours.StartTime;
                    data.endTime = u.WorkingHours.EndTime;
                    data.timeZone = u.WorkingHours.TimeZone;
                }

                // events: prep filter
                FolderId folderId = new FolderId(WellKnownFolderName.Calendar, new Mailbox(outlook.Mailbox));
                CalendarFolder calendar = CalendarFolder.Bind(service, folderId, new PropertySet());
                CalendarView cView = new CalendarView(windowBeg, windowEnd)
                {
                    PropertySet = new PropertySet(
                        AppointmentSchema.Subject,
                        AppointmentSchema.DateTimeCreated,
                        AppointmentSchema.Start,
                        AppointmentSchema.End,
                        AppointmentSchema.Duration
                        )
                };

                // events: get list
                data.events = calendar
                    .FindAppointments(cView)
                    .OrderBy(i => i.Start)
                    .ThenBy(i => i.DateTimeCreated)
                    .ThenBy(i => i.Subject)
                    //.Take(Math.Max(1, outlook.ShowEvents))
                    .Select(a => new EventEntry
                    {
                        Subject = a.Subject,
                        CreatedOn = a.DateTimeCreated,
                        Starts = a.Start,
                        Ends = a.End,
                        Duration = a.Duration,
                        Today = locationToday,
                    })
                    //.ToList()
                    ;
            }

            #endregion

            #region -------- EWS Callbacks --------

            private static bool RedirectionUrlValidationCallback(string redirectionUrl)
            {
                // The default for the validation callback is to reject the URL.
                bool result = false;

                Uri redirectionUri = new Uri(redirectionUrl);

                // Validate the contents of the redirection URL. In this simple validation
                // callback, the redirection URL is considered valid if it is using HTTPS
                // to encrypt the authentication credentials. 
                if (redirectionUri.Scheme == "https")
                {
                    result = true;
                }
                return result;
            }

            private static bool CertificateValidationCallBack(
                object sender,
                System.Security.Cryptography.X509Certificates.X509Certificate certificate,
                System.Security.Cryptography.X509Certificates.X509Chain chain,
                System.Net.Security.SslPolicyErrors sslPolicyErrors
                )
            {
                //return true;
                // If the certificate is a valid, signed certificate, return true.
                if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
                {
                    return true;
                }

                // If there are errors in the certificate chain, look at each error to determine the cause.
                if ((sslPolicyErrors & System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors) != 0)
                {
                    if (chain != null && chain.ChainStatus != null)
                    {
                        foreach (System.Security.Cryptography.X509Certificates.X509ChainStatus status
                            in chain.ChainStatus)
                        {
                            if ((certificate.Subject == certificate.Issuer) &&
                                (status.Status ==
                                    System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.UntrustedRoot))
                            {
                                // Self-signed certificates with an untrusted root are valid. 
                                continue;
                            }
                            else
                            {
                                if (status.Status !=
                                        System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.NoError)
                                {
                                    // If there are any other errors in the certificate chain, the certificate is invalid,
                                    // so the method returns false.
                                    return false;
                                }
                            }
                        }
                    }

                    // When processing reaches this line, the only errors in the certificate chain are 
                    // untrusted root errors for self-signed certificates. These certificates are valid
                    // for default Exchange server installations, so return true.
                    return true;
                }

                // In all other cases, return false.
                return false;
            }

            #endregion
        }

        #endregion

        #region -------- EventEntry --------

        private class EventEntry : IComparable<EventEntry>
        {
            public string Subject { get; set; }
            public DateTime CreatedOn { get; set; }
            public DateTime Starts { get; set; }
            public DateTime Ends { get; set; }
            public TimeSpan Duration { get; set; }
            public DateTime Today { get; set; }

            public int CompareTo(EventEntry rhs)
            {
                int startsCheck = this.Starts.CompareTo(rhs.Starts);
                if (startsCheck == 0)
                {
                    int createdCheck = this.CreatedOn.CompareTo(rhs.CreatedOn);
                    if (createdCheck == 0)
                        return this.Subject.CompareTo(rhs.Subject);
                    else
                        return createdCheck;
                }
                else
                    return startsCheck;
            }
        }

        private class EventEntryConverter : JavaScriptConverter
        {
            public override IEnumerable<Type> SupportedTypes
            {
                get
                {
                    return new[] { typeof(EventEntry) };
                }
            }

            public static object[] Head()
            {
                return new[] 
                {
                    new {cls = "col1", name = Resources.Outlook_Event},
                    new {cls = "col2", name = Resources.Outlook_Starts},
                    new {cls = "col3", name = Resources.Outlook_Ends},
                    new {cls = "col4", name = Resources.Outlook_Duration}
                };
            }

            public override IDictionary<string, object> Serialize(object obj, JavaScriptSerializer serializer)
            {
                IDictionary<string, object> serialized = new Dictionary<string, object>();
                EventEntry evt = obj as EventEntry;
                if (evt != null)
                {
                    DateTime tomorrow = evt.Today.AddDays(1);
                    serialized["col1"] = evt.Subject;
                    serialized["col2"] = (evt.Starts >= tomorrow ? evt.Starts.ToShortDateString() + " " : "") + evt.Starts.ToShortTimeString();
                    serialized["col3"] = (evt.Ends >= tomorrow ? evt.Ends.ToShortDateString() + " " : "") + evt.Ends.ToShortTimeString();
                    if ((int)evt.Duration.TotalHours > 0)
                        serialized["col4"] = string.Format(Resources.Outlook_HrsMin, (int)evt.Duration.TotalHours, evt.Duration.Minutes);
                    else
                        serialized["col4"] = string.Format(Resources.Outlook_Min, evt.Duration.Minutes);
                }
                return serialized;
            }

            public override object Deserialize(IDictionary<string, object> dictionary, Type type, JavaScriptSerializer serializer)
            {
                throw new NotImplementedException();
            }
        }

        #endregion
    }
}