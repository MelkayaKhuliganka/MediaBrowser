﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace MediaBrowser.Controller.Entities.Audio
{
    /// <summary>
    /// Class MusicAlbum
    /// </summary>
    public class MusicAlbum : Folder, IHasAlbumArtist, IHasArtist, IHasMusicGenres
    {
        public MusicAlbum()
        {
            Artists = new List<string>();
        }

        public string LastFmImageUrl { get; set; }

        /// <summary>
        /// Songs will group into us so don't also include us in the index
        /// </summary>
        /// <value><c>true</c> if [include in index]; otherwise, <c>false</c>.</value>
        [IgnoreDataMember]
        public override bool IncludeInIndex
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Override this to true if class should be grouped under a container in indicies
        /// The container class should be defined via IndexContainer
        /// </summary>
        /// <value><c>true</c> if [group in index]; otherwise, <c>false</c>.</value>
        [IgnoreDataMember]
        public override bool GroupInIndex
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// The unknwon artist
        /// </summary>
        private static readonly MusicArtist UnknwonArtist = new MusicArtist { Name = "<Unknown>" };

        /// <summary>
        /// Override this to return the folder that should be used to construct a container
        /// for this item in an index.  GroupInIndex should be true as well.
        /// </summary>
        /// <value>The index container.</value>
        [IgnoreDataMember]
        public override Folder IndexContainer
        {
            get { return Parent as MusicArtist ?? UnknwonArtist; }
        }

        /// <summary>
        /// Determines whether the specified artist has artist.
        /// </summary>
        /// <param name="artist">The artist.</param>
        /// <returns><c>true</c> if the specified artist has artist; otherwise, <c>false</c>.</returns>
        public bool HasArtist(string artist)
        {
            return string.Equals(AlbumArtist, artist, StringComparison.OrdinalIgnoreCase)
                   || Artists.Contains(artist, StringComparer.OrdinalIgnoreCase);
        }

        public string AlbumArtist { get; set; }

        public List<string> Artists { get; set; }
    }

    public class MusicAlbumDisc : Folder
    {

    }
}
