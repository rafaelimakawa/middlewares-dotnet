﻿using System.ComponentModel.DataAnnotations;

namespace Looplex.DotNet.Middlewares.ScimV2.Entities.Users
{
    public class Address
    {
        public string Formatted { get; set; }

        public string StreetAddress { get; set; }

        public string Locality { get; set; }

        public string Region { get; set; }

        public string PostalCode { get; set; }

        public string Country { get; set; }

        [RegularExpression("work|home|other", ErrorMessage = "Type must be either 'work', 'home', or 'other'.")]
        public string Type { get; set; }
    }
}
