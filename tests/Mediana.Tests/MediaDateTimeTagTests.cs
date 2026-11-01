// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Metadata;

namespace Mediana.Tests
{
    public class MediaDateTimeTagTests
    {
        [Theory]
        [InlineData(DateTimeKind.Utc)]
        [InlineData(DateTimeKind.Unspecified)]
        public void CalendarFieldsPreserveTheirDeclaredKindAndImmutableSnapshot(DateTimeKind kind)
        {
            DateTime value = new(2026, 3, 7, 12, 34, 56, kind);
            MediaDateTimeTag tag = new("native", "creation_time", value, "track/header");
            List<MediaDateTimeTag> dates = [tag];
            ContainerInfo movie = new("container", dates: dates);
            ContainerStreamInfo track = new(0, dates: dates);
            dates.Clear();
            Assert.Same(tag, Assert.Single(movie.Dates!));
            Assert.Same(tag, Assert.Single(track.Dates!));
            Assert.Equal(value, tag.Value);
            Assert.Equal(kind, tag.Value.Kind);
            Assert.Equal("native", tag.Namespace);
            Assert.Equal("creation_time", tag.Name);
            Assert.Equal("track/header", tag.SourcePath);
            Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<MediaDateTimeTag>>(movie.Dates).Clear());
            Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<MediaDateTimeTag>>(track.Dates).Clear());
        }

        [Fact]
        public void UnavailableAndEmptyDatesRemainDistinctAndInvalidValuesFail()
        {
            Assert.Null(new ContainerInfo("container").Dates);
            Assert.Null(new ContainerStreamInfo(0).Dates);
            Assert.Empty(new ContainerInfo("container", dates: []).Dates!);
            Assert.Empty(new ContainerStreamInfo(0, dates: []).Dates!);
            Assert.Throws<ArgumentException>(() => new MediaDateTimeTag("", "created", DateTime.MinValue));
            Assert.Throws<ArgumentException>(() => new MediaDateTimeTag("native", "", DateTime.MinValue));
            Assert.Throws<ArgumentException>(() => new MediaDateTimeTag("native", "created", new(2026, 1, 1, 0, 0, 0, DateTimeKind.Local)));
            Assert.Equal("dates", Assert.Throws<ArgumentException>(() => new ContainerInfo("container", dates: new MediaDateTimeTag[] { null! })).ParamName);
            Assert.Equal("dates", Assert.Throws<ArgumentException>(() => new ContainerStreamInfo(0, dates: new MediaDateTimeTag[] { null! })).ParamName);
        }
    }
}
